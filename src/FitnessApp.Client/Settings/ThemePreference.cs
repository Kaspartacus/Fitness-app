using FitnessApp.Client.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace FitnessApp.Client.Settings;

// The cache is a display hint only. It is never used as input to a settings write.
public sealed class ThemePreference(
    SettingsClient settings, InMemoryAuthenticationStateProvider authentication, IJSRuntime javaScript) : IDisposable
{
    public string Current { get; private set; } = "dark";
    public bool IsReady { get; private set; }
    public bool IsSaving { get; private set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    public Task Synchronization { get; private set; } = Task.CompletedTask;
    private string? session;
    private long generation = -1;
    private long revision;
    private bool initialized;

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        Current = await javaScript.InvokeAsync<string>("fitnessTheme.current");
        authentication.AuthenticationStateChanged += AuthenticationChanged;
    }

    private void AuthenticationChanged(Task<AuthenticationState> state) => Synchronization = SynchronizeAsync();

    public async Task SynchronizeAsync()
    {
        var nextSession = authentication.SessionId;
        var nextGeneration = authentication.Generation;
        if (session == nextSession && generation == nextGeneration && (IsReady || IsSaving)) return;
        if (session == nextSession && generation == nextGeneration && nextSession is not null) return;
        session = nextSession;
        generation = nextGeneration;
        var expectedRevision = ++revision;
        IsReady = false;
        IsLoading = session is not null;
        IsSaving = false;
        Error = null;
        Changed?.Invoke();
        if (session is null)
        {
            await ApplyAsync("dark");
            return;
        }
        var result = await settings.GetOverviewAsync();
        if (!IsCurrent(expectedRevision)) return;
        if (result.Succeeded && result.Value is { } overview)
        {
            await ApplyAsync(overview.Theme);
            if (!IsCurrent(expectedRevision)) return;
            IsReady = true;
        }
        else
        {
            // Do not offer a write until this account's stored preference is known.
            await ApplyAsync("dark");
            if (!IsCurrent(expectedRevision)) return;
            Error = result.Error ?? "Udseendet kunne ikke indlæses. Prøv igen.";
        }
        IsLoading = false;
        Changed?.Invoke();
    }

    public async Task ReloadAsync()
    {
        generation = -1;
        Synchronization = SynchronizeAsync();
        await Synchronization;
    }

    public async Task SelectAsync(string theme)
    {
        if (!IsReady || IsSaving || authentication.AccessToken is null || theme is not ("light" or "dark")) return;
        var expectedRevision = revision;
        var previous = Current;
        IsSaving = true;
        Error = null;
        Changed?.Invoke();
        await ApplyAsync(theme);
        if (!IsCurrent(expectedRevision)) return;
        var result = await settings.UpdateThemeAsync(theme);
        if (!IsCurrent(expectedRevision)) return;
        if (!result.Succeeded)
        {
            await ApplyAsync(previous);
            if (!IsCurrent(expectedRevision)) return;
            Error = result.Error ?? "Udseendet kunne ikke gemmes. Prøv igen.";
        }
        IsSaving = false;
        Changed?.Invoke();
    }

    private bool IsCurrent(long expectedRevision) => revision == expectedRevision &&
        session == authentication.SessionId && generation == authentication.Generation;

    private async Task ApplyAsync(string theme)
    {
        Current = theme == "light" ? "light" : "dark";
        try { await javaScript.InvokeVoidAsync("fitnessTheme.apply", Current); }
        catch (JSException) { /* Storage restrictions must not break authentication or settings. */ }
    }

    public void Dispose() => authentication.AuthenticationStateChanged -= AuthenticationChanged;
}
