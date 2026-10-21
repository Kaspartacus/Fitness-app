using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Diary;
using FitnessApp.Contracts.Diary;
using DiaryPage = FitnessApp.Client.Pages.Diary;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualActivityHandlerPreservesDraftAndGrantDuringRecoverableOutage(bool transportFailure)
    {
        const string grant = "synthetic-memory-only-grant";
        var paths = new List<string>(); var activityAvailable = false;
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; paths.Add(path);
            if (path.EndsWith("/unlock")) return Json(new DiaryUnlockResponse(grant));
            if (path.EndsWith("/activity"))
            {
                if (!activityAvailable)
                {
                    if (transportFailure) throw new HttpRequestException("Synthetic offline connection");
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                return Json(true);
            }
            Assert.Equal(grant, Assert.Single(request.Headers.GetValues("X-Diary-Grant")));
            return Json(new DiaryStatusResponse(true, true, true, new(2026, 10, 22), "Europe/Copenhagen"));
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        Assert.True((await api.UnlockAsync("x")).Succeeded);
        var locked = 0; api.Locked += () => locked++;
        var page = Page(api, new(2026, 10, 21));
        Set(page, "text", "Synthetic unsaved draft"); Set(page, "baseline", "Synthetic saved version");
        await page.DiaryActivity();
        Assert.Equal(0, locked); Assert.DoesNotContain("/api/diary/lock", paths);
        Assert.Equal("Synthetic unsaved draft", Field<string>(page, "text"));
        // Foreground activity can recover without re-unlocking and without replacing the draft.
        activityAvailable = true;
        await page.DiaryActivity();
        Assert.Equal(new DateOnly(2026, 10, 22), Field<DiaryStatusResponse>(page, "state").Today);
        Assert.Equal("Synthetic unsaved draft", Field<string>(page, "text")); Assert.Equal(0, locked);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(423)]
    public async Task ConfirmedActivityDenialInvalidatesTheMemoryGrant(int status)
    {
        var denied = false; var carriedRevokedGrant = false;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/unlock")) return Json(new DiaryUnlockResponse("synthetic-grant"));
            if (!denied) { denied = true; return new((HttpStatusCode)status); }
            carriedRevokedGrant = request.Headers.Contains("X-Diary-Grant");
            return Json(new DiaryStatusResponse(true, true, false, new(2026, 10, 21), "Europe/Copenhagen"));
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        await api.UnlockAsync("x"); var locked = 0; api.Locked += () => locked++;
        var page = Page(api, new(2026, 10, 21));
        await page.DiaryActivity();
        Assert.Equal(1, locked);
        await api.StateAsync(); Assert.False(carriedRevokedGrant);
    }

    [Theory]
    [InlineData("2026-10-21", "2026-10-22", "Europe/Copenhagen")]
    [InlineData("2026-10-22", "2026-10-21", "America/New_York")]
    public async Task ActualTodayActionConfirmsDraftBeforeRefreshingServerDayAndOpeningNewDate(string previous, string current, string zone)
    {
        var oldDay = DateOnly.Parse(previous); var newDay = DateOnly.Parse(current);
        var paths = new List<string>(); var savedVersion = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; paths.Add(path);
            if (path.EndsWith("/state")) return Json(new DiaryStatusResponse(true, false, true, newDay, zone));
            if (path.Contains("/entries/"))
            {
                Assert.EndsWith(newDay.ToString("yyyy-MM-dd"), path);
                return Json(new DiaryEntryResponse(newDay, "Synthetic saved page on authoritative date", savedVersion));
            }
            return Json(Array.Empty<DiaryDateResponse>());
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = Page(api, oldDay);
        Set(page, "selected", oldDay); Set(page, "text", "Synthetic unsaved draft"); Set(page, "baseline", "Old saved page");
        Invoke(page, "WriteToday");
        Assert.Equal("discard", Field<string>(page, "dialog")); Assert.Empty(paths);
        Assert.Equal("Synthetic unsaved draft", Field<string>(page, "text"));
        // Cancelling the shared confirmation must leave the draft and selection alone.
        Invoke(page, "CloseDialog");
        Assert.Equal("Synthetic unsaved draft", Field<string>(page, "text")); Assert.Equal(oldDay, Field<DateOnly>(page, "selected"));
        Invoke(page, "WriteToday");
        await (Task)Invoke(page, "DiscardAsync")!;
        Assert.Equal(newDay, Field<DateOnly>(page, "selected"));
        Assert.Equal(newDay, Field<DiaryStatusResponse>(page, "state").Today);
        Assert.Equal(zone, Field<DiaryStatusResponse>(page, "state").TimeZoneId);
        Assert.Equal("Synthetic saved page on authoritative date", Field<string>(page, "text"));
        Assert.Equal("/api/diary/state", paths.First());
    }

    [Fact]
    public async Task RefreshingDateControlBoundsDoesNotResetSelectedDateOrDraft()
    {
        var current = new DateOnly(2026, 10, 22); var selected = current.AddDays(-4);
        using var http = new HttpClient(new Handler(_ => Json(new DiaryStatusResponse(true, false, true, current, "Europe/Copenhagen"))))
            { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = Page(api, current.AddDays(-1));
        Set(page, "selected", selected); Set(page, "text", "Synthetic unsaved draft"); Set(page, "baseline", "Saved version");
        await (Task)Invoke(page, "RefreshDayForControlsAsync")!;
        Assert.Equal(current, Field<DiaryStatusResponse>(page, "state").Today);
        Assert.Equal(selected, Field<DateOnly>(page, "selected"));
        Assert.Equal("Synthetic unsaved draft", Field<string>(page, "text"));
        Assert.Equal("Saved version", Field<string>(page, "baseline"));
    }

    [Fact]
    public async Task DelayedTodayRefreshCannotReplaceAnInterveningDraftWithoutConfirmation()
    {
        var oldDay = new DateOnly(2026, 10, 21); var today = oldDay.AddDays(1);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedState = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entryRequests = 0;
        using var http = new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/state"))
            {
                requestedState.TrySetResult();
                return await response.Task;
            }
            entryRequests++;
            return Json(new DiaryEntryResponse(today, "Synthetic other page", Guid.NewGuid()));
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = Page(api, oldDay);
        Set(page, "text", "Synthetic saved page"); Set(page, "baseline", "Synthetic saved page");
        // This is the production operation used by both Skriv i dag and date selection.
        var opening = (Task)typeof(DiaryPage).GetMethod("OpenDateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, new object?[] { null })!;
        await requestedState.Task;
        Assert.True(Field<bool>(page, "busy"));
        // Simulate input already queued before the disabled controls were rendered.
        Set(page, "text", "Synthetic intervening unsaved draft");
        response.SetResult(Json(new DiaryStatusResponse(true, false, true, today, "Europe/Copenhagen")));
        await opening;
        Assert.False(Field<bool>(page, "busy"));
        Assert.Equal("discard", Field<string>(page, "dialog"));
        Assert.Equal("Synthetic intervening unsaved draft", Field<string>(page, "text"));
        Assert.Equal(oldDay, Field<DateOnly>(page, "selected")); Assert.Equal(0, entryRequests);
        Invoke(page, "CloseDialog");
        Assert.Equal("Synthetic intervening unsaved draft", Field<string>(page, "text"));
    }

    private static DiaryPage Page(DiaryClient api, DateOnly today)
    {
        var page = new DiaryPageProbe();
        typeof(DiaryPage).GetProperty("DiaryApi", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, api);
        Set(page, "state", new DiaryStatusResponse(true, true, true, today, "Europe/Copenhagen"));
        Set(page, "selected", today); Set(page, "month", new DateOnly(today.Year, today.Month, 1));
        page.Attach();
        return page;
    }
    // Exercise the production component handlers without starting a browser or adding a UI-test dependency.
    private static void Set(DiaryPage page, string name, object value) => typeof(DiaryPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, value);
    private static T Field<T>(DiaryPage page, string name) => (T)typeof(DiaryPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
    private static object? Invoke(DiaryPage page, string name) => typeof(DiaryPage).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
