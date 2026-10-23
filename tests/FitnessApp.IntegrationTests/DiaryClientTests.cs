using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Diary;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Diary;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using DiaryPage = FitnessApp.Client.Pages.Diary;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryClientTests
{
    [Fact]
    public async Task ProductionDisposalCompletesAndClearsLocallyWhileServerLockIsStillPending()
    {
        const string oldGrant = "synthetic-old-memory-grant";
        var lockRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? lockGrant = null;
        var stateCarriedGrant = true;
        using var http = new HttpClient(new AsyncHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/unlock")) return Json(new DiaryUnlockResponse(oldGrant));
            if (path.EndsWith("/lock"))
            {
                lockGrant = Assert.Single(request.Headers.GetValues("X-Diary-Grant"));
                lockRequested.TrySetResult();
                var response = await lockResponse.Task;
                lockFinished.TrySetResult();
                return response;
            }
            stateCarriedGrant = request.Headers.Contains("X-Diary-Grant");
            return Json(new DiaryStatusResponse(true, true, false, new(2026, 10, 22), "Europe/Copenhagen"));
        })) { BaseAddress = new("https://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        await api.UnlockAsync("x");
        var page = Page(api, new(2026, 10, 22));
        typeof(DiaryPage).GetProperty("JS", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, new NoopJsRuntime());
        Set(page, "text", "Synthetic private draft"); Set(page, "baseline", "Synthetic saved entry");
        Set(page, "code", "synthetic-code");

        try
        {
            // Call the production method directly; the probe substitutes only its own lifecycle.
            await page.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await lockRequested.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(lockResponse.Task.IsCompleted);
            Assert.Equal(oldGrant, lockGrant);
            Assert.Equal("", Field<string>(page, "text"));
            Assert.Equal("", Field<string>(page, "baseline"));
            Assert.Equal("", Field<string>(page, "code"));
            await api.StateAsync();
            Assert.False(stateCarriedGrant);
        }
        finally
        {
            lockResponse.TrySetResult(Json(true));
            await lockFinished.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await ((DiaryPageProbe)page).DisposeRendererAsync();
        }
    }

    [Fact]
    public async Task ClosingDuringNearExpiryRefreshDoesNotCancelOrSuspendTheLoginSession()
    {
        var state = new InMemoryAuthenticationStateProvider();
        var user = new AuthenticatedUserResponse("synthetic@example.test", "Synthetic user", ["User"]);
        state.SetSession(new("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(15), user, "synthetic-session"));
        var navigation = new DiaryNavigationManager();
        var refreshRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken refreshCancellation = default;
        using var sessionHttp = new HttpClient(new CancellableHandler(async (request, cancellationToken) =>
        {
            Assert.EndsWith("/api/auth/refresh", request.RequestUri!.AbsolutePath);
            refreshCancellation = cancellationToken;
            refreshRequested.TrySetResult();
            return await refreshResponse.Task.WaitAsync(cancellationToken);
        })) { BaseAddress = new(navigation.BaseUri), Timeout = Timeout.InfiniteTimeSpan };
        var authentication = new AuthenticationClient(new Factory(sessionHttp), state, navigation);
        using var authorization = new ApiAuthorizationHandler(state, navigation, authentication)
        {
            InnerHandler = new Handler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/unlock"))
                    return Json(new DiaryUnlockResponse("synthetic-grant"));
                Assert.EndsWith("/api/diary/lock", request.RequestUri!.AbsolutePath);
                Assert.Equal("synthetic-grant", Assert.Single(request.Headers.GetValues("X-Diary-Grant")));
                lockFinished.TrySetResult();
                return Json(true);
            })
        };
        using var diaryHttp = new HttpClient(authorization) { BaseAddress = new(navigation.BaseUri), Timeout = Timeout.InfiniteTimeSpan };
        using var api = new DiaryClient(new Factory(diaryHttp), state);
        await api.UnlockAsync("x");
        state.SetSession(new("synthetic-token", DateTimeOffset.UtcNow.AddSeconds(5), user, "synthetic-session"));
        var page = Page(api, new(2026, 10, 22));
        typeof(DiaryPage).GetProperty("JS", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, new NoopJsRuntime());
        try
        {
            await page.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(1));
            // Exceed the former close-specific timeout while the real authentication refresh waits.
            await Task.Delay(TimeSpan.FromMilliseconds(2200));
            Assert.False(refreshCancellation.IsCancellationRequested);
            Assert.Equal("synthetic-token", state.AccessToken);
            Assert.False(authentication.IsUnavailable);
        }
        finally
        {
            refreshResponse.TrySetResult(Json(new LoginResponse("synthetic-refreshed-token",
                DateTimeOffset.UtcNow.AddMinutes(15), user, "synthetic-session")));
            await lockFinished.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await ((DiaryPageProbe)page).DisposeRendererAsync();
        }
        Assert.Equal("synthetic-refreshed-token", state.AccessToken);
        Assert.False(authentication.IsUnavailable);
    }

    [Fact]
    public async Task DelayedOldLockDenialCannotClearANewerUnlockGrant()
    {
        var oldLockResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unlocks = 0; var locks = 0; string? stateGrant = null;
        using var http = new HttpClient(new AsyncHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/unlock"))
                return Task.FromResult(Json(new DiaryUnlockResponse(++unlocks == 1 ? "synthetic-old-grant" : "synthetic-new-grant")));
            if (path.EndsWith("/lock"))
            {
                Assert.Equal("synthetic-old-grant", Assert.Single(request.Headers.GetValues("X-Diary-Grant")));
                return oldLockResponse.Task;
            }
            stateGrant = Assert.Single(request.Headers.GetValues("X-Diary-Grant"));
            return Task.FromResult(Json(new DiaryStatusResponse(true, true, true, new(2026, 10, 22), "Europe/Copenhagen")));
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        await api.UnlockAsync("x"); api.Locked += () => locks++;
        var locking = api.LockAsync();
        try
        {
            Assert.Equal(1, locks);
            Assert.True((await api.UnlockAsync("x")).Succeeded);
            oldLockResponse.SetResult(new(HttpStatusCode.Locked));
            await locking;
            await api.StateAsync();
            Assert.Equal("synthetic-new-grant", stateGrant);
            Assert.Equal(1, locks);
        }
        finally
        {
            if (!oldLockResponse.Task.IsCompleted) oldLockResponse.TrySetResult(new(HttpStatusCode.Locked));
            await locking.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutOfCreationWindowSelectionCannotOpenAnEmptyDraftButCanOpenARetainedEntry(bool existingEntry)
    {
        var today = new DateOnly(2025, 2, 28); var requested = new DateOnly(2024, 2, 29);
        var bounds = new DiaryStatusResponse(true, false, true, today, "Asia/Tokyo")
        {
            MinimumNewEntryDate = new(2024, 3, 1)
        };
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/state")) return Json(bounds);
            if (path.Contains("/entries/")) return existingEntry
                ? Json(new DiaryEntryResponse(requested, "Synthetic retained entry", Guid.NewGuid()))
                : new(HttpStatusCode.NotFound);
            return Json(Array.Empty<DiaryDateResponse>());
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = Page(api, today);
        Set(page, "state", bounds);
        Set(page, "text", "Synthetic current entry"); Set(page, "baseline", "Synthetic current entry");
        // Both a native onchange and a saved-date list button reach this production operation.
        await (Task)typeof(DiaryPage).GetMethod("RequestDate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, new object[] { requested })!;
        Assert.Equal(existingEntry ? requested : today, Field<DateOnly>(page, "selected"));
        Assert.Equal(existingEntry ? "Synthetic retained entry" : "Synthetic current entry", Field<string>(page, "text"));
        if (!existingEntry) Assert.False(string.IsNullOrEmpty(Field<string?>(page, "error")));
        await ((DiaryPageProbe)page).DisposeRendererAsync();
    }

    [Fact]
    public async Task FutureLocalMonthBrowsingOpensRetainedEntriesButNotEmptyFutureDrafts()
    {
        var today = new DateOnly(2026, 11, 30); var retainedDate = new DateOnly(2026, 12, 1);
        var bounds = new DiaryStatusResponse(true, false, true, today, "America/Los_Angeles")
        {
            MinimumNewEntryDate = new(2025, 12, 1)
        };
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/state")) return Json(bounds);
            if (path.Contains("/dates/"))
            {
                Assert.EndsWith("/dates/2026/12", path);
                return Json(new[] { new DiaryDateResponse(retainedDate, Guid.NewGuid()) });
            }
            return path.EndsWith("/entries/2026-12-01")
                ? Json(new DiaryEntryResponse(retainedDate, "Synthetic retained December entry", Guid.NewGuid()))
                : new(HttpStatusCode.NotFound);
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = Page(api, today);
        Set(page, "state", bounds);
        await (Task)typeof(DiaryPage).GetMethod("ChangeMonthAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, new object[] { new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "2026-12" } })!;
        Assert.Equal(retainedDate, Assert.Single(Field<DiaryDateResponse[]>(page, "dates")).Date);
        await (Task)typeof(DiaryPage).GetMethod("RequestDate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, new object[] { retainedDate })!;
        Assert.Equal(retainedDate, Field<DateOnly>(page, "selected"));
        Assert.Equal("Synthetic retained December entry", Field<string>(page, "text"));
        await (Task)typeof(DiaryPage).GetMethod("RequestDate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, new object[] { retainedDate.AddDays(1) })!;
        Assert.Equal(retainedDate, Field<DateOnly>(page, "selected"));
        Assert.Equal("Synthetic retained December entry", Field<string>(page, "text"));
        Assert.False(string.IsNullOrEmpty(Field<string?>(page, "error")));
        await ((DiaryPageProbe)page).DisposeRendererAsync();
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
    private sealed class CancellableHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
    private sealed class DiaryNavigationManager : NavigationManager
    {
        public DiaryNavigationManager() => Initialize("https://localhost/", "https://localhost/dagbog");
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
