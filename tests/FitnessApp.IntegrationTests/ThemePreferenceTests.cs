using System.Net;
using System.Net.Http.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Settings;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Settings;
using Microsoft.JSInterop;

namespace FitnessApp.IntegrationTests;

public sealed class ThemePreferenceTests
{
    [Fact]
    public async Task CachedChoiceIsNeverUploadedAndNewAccountLoadsItsOwnPreference()
    {
        var authentication = new InMemoryAuthenticationStateProvider();
        var javaScript = new ThemeJavaScript { Theme = "light" };
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Overview("dark"));
        })) { BaseAddress = new Uri("https://fitness.example/") };
        using var theme = new ThemePreference(new SettingsClient(new Factory(http)), authentication, javaScript);
        await theme.InitializeAsync();
        Assert.Equal("light", theme.Current);
        await UntilReadyAsync(theme, () => authentication.SetSession(Session("account-b")));
        Assert.Equal("dark", javaScript.Theme);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task DelayedSaveFailureCannotRollbackTheNextAccountsTheme()
    {
        var authentication = new InMemoryAuthenticationStateProvider();
        var javaScript = new ThemeJavaScript();
        var pendingSave = new TaskCompletionSource<HttpResponseMessage>();
        var saveStarted = new TaskCompletionSource();
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Overview("dark"));
            saveStarted.SetResult();
            return pendingSave.Task;
        })) { BaseAddress = new Uri("https://fitness.example/") };
        using var theme = new ThemePreference(new SettingsClient(new Factory(http)), authentication, javaScript);
        await theme.InitializeAsync();
        await UntilReadyAsync(theme, () => authentication.SetSession(Session("account-a")));
        var save = theme.SelectAsync("light");
        await saveStarted.Task;
        Assert.Equal("light", javaScript.Theme); // Optimistic application before the HTTP result.
        authentication.BeginTransition();
        await UntilReadyAsync(theme, () => authentication.SetSession(Session("account-b")));
        pendingSave.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await save;
        Assert.Equal("dark", theme.Current);
        Assert.Equal("dark", javaScript.Theme);
        Assert.Null(theme.Error);
        Assert.False(theme.IsSaving);
    }

    [Fact]
    public async Task DelayedOverviewCannotReplaceNextAccountsPreference()
    {
        var authentication = new InMemoryAuthenticationStateProvider();
        var javaScript = new ThemeJavaScript();
        var firstRead = new TaskCompletionSource<HttpResponseMessage>();
        var count = 0;
        using var http = new HttpClient(new Handler(_ => ++count == 1 ? firstRead.Task : Task.FromResult(Overview("dark"))))
        { BaseAddress = new Uri("https://fitness.example/") };
        using var theme = new ThemePreference(new SettingsClient(new Factory(http)), authentication, javaScript);
        await theme.InitializeAsync();
        authentication.SetSession(Session("account-a"));
        var firstLoad = theme.Synchronization;
        authentication.BeginTransition();
        await UntilReadyAsync(theme, () => authentication.SetSession(Session("account-b")));
        firstRead.SetResult(Overview("light"));
        await firstLoad;
        Assert.Equal("dark", theme.Current);
        Assert.Equal("dark", javaScript.Theme);
    }

    private static async Task UntilReadyAsync(ThemePreference theme, Action action)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed() { if (theme.IsReady) ready.TrySetResult(); }
        theme.Changed += Changed;
        try { action(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { theme.Changed -= Changed; }
    }

    private static LoginResponse Session(string id) => new("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(15),
        new AuthenticatedUserResponse("synthetic@example.test", "Syntetisk", []), id);

    private static HttpResponseMessage Overview(string theme) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new SettingsOverviewResponse(
            new("Syntetisk", null, null, new(null, null, null, null, null)),
            new(true, true, true, true, false, "Europe/Copenhagen"), new(false, null), 0, theme))
    };

    private sealed class Factory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class ThemeJavaScript : IJSRuntime
    {
        public string Theme { get; set; } = "dark";
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "fitnessTheme.current") return ValueTask.FromResult((TValue)(object)Theme);
            if (identifier == "fitnessTheme.apply") Theme = (string)args![0]!;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
