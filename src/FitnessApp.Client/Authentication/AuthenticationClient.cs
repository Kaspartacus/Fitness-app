using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FitnessApp.Client.Authentication;

public sealed class AuthenticationClient(
    IHttpClientFactory httpClientFactory,
    InMemoryAuthenticationStateProvider authenticationState,
    NavigationManager navigationManager,
    IJSRuntime? javaScript = null)
{
    public const string ClientName = "FitnessAppApi";
    private const string GenericLoginFailure =
        "E-mail eller adgangskode er forkert, eller kontoen har ikke adgang.";

    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private DotNetObjectReference<AuthenticationClient>? reference;
    public bool IsUnavailable { get; private set; }
    public bool LogoutPending { get; private set; }
    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        try
        {
        if (javaScript is not null && reference is null)
        {
            reference = DotNetObjectReference.Create(this);
            await javaScript.InvokeVoidAsync("fitnessSession.initialize", reference);
        }
        LogoutPending = javaScript is not null && await javaScript.InvokeAsync<bool>("fitnessSession.pendingLogout");
        if (LogoutPending) return;
        var foreground = javaScript is not null && await javaScript.InvokeAsync<bool>("fitnessSession.foregroundOpening");
        await RefreshAsync(foreground);
        }
        catch (JSException)
        {
            authenticationState.SuspendSession();
            IsUnavailable = true;
            StateChanged?.Invoke();
        }
    }

    [JSInvokable]
    public Task SessionChanged()
    {
        authenticationState.BeginTransition();
        IsUnavailable = true;
        StateChanged?.Invoke();
        navigationManager.NavigateTo(navigationManager.Uri, forceLoad: true);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task UserActivity()
    {
        if (authenticationState.AccessToken is not null) await RefreshAsync(userActivity: true);
    }

    public async Task<CurrentUserStatus> RefreshAsync(bool userActivity = false, CancellationToken cancellationToken = default)
    {
        var generation = authenticationState.Generation;
        var expectedSession = authenticationState.SessionId;
        var originalToken = authenticationState.AccessToken;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (generation != authenticationState.Generation) return CurrentUserStatus.Unavailable;
            if (!userActivity && authenticationState.AccessToken is not null &&
                authenticationState.AccessToken != originalToken && authenticationState.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return CurrentUserStatus.Succeeded;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var response = await SessionRequestAsync("refresh", new RefreshRequest(userActivity, expectedSession), cancellationToken);
                if (generation != authenticationState.Generation) return CurrentUserStatus.Unavailable;
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) continue;
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    authenticationState.ClearSession();
                    IsUnavailable = false;
                    StateChanged?.Invoke();
                    return CurrentUserStatus.Unauthorized;
                }
                if (!response.IsSuccessStatusCode) break;
                var session = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
                if (session is null || generation != authenticationState.Generation) break;
                authenticationState.SetSession(session);
                IsUnavailable = false;
                StateChanged?.Invoke();
                return CurrentUserStatus.Succeeded;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or JSException) { }
        finally { refreshLock.Release(); }
        if (generation == authenticationState.Generation)
        {
            authenticationState.SuspendSession();
            IsUnavailable = true;
            StateChanged?.Invoke();
        }
        return CurrentUserStatus.Unavailable;
    }

    public async Task<string?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // An anonymous recovery page can submit while startup restoration is still running.
        // Complete that refresh before capturing the intentional login's generation. Keep
        // logout/cross-tab transitions independent so they can still invalidate this result.
        var intendedTransition = authenticationState.TransitionGeneration;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (intendedTransition != authenticationState.TransitionGeneration) return "Sessionen er ændret. Prøv igen.";
            var generation = authenticationState.Generation;
            using var response = await SessionRequestAsync("login", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    ? "For mange loginforsøg. Prøv igen senere." : GenericLoginFailure;
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                ?? throw new InvalidOperationException("The login response was empty.");
            if (generation != authenticationState.Generation) return "Sessionen er ændret. Prøv igen.";
            authenticationState.BeginTransition();
            authenticationState.SetSession(login);
            IsUnavailable = false;
            LogoutPending = false;
            StateChanged?.Invoke();
            return null;
        }
        finally { refreshLock.Release(); }
    }

    private async Task<HttpResponseMessage> SessionRequestAsync(string path, object? body, CancellationToken cancellationToken)
    {
        if (javaScript is not null)
        {
            var result = await javaScript.InvokeAsync<SessionHttpResult>("fitnessSession.request", cancellationToken, path, body);
            return new HttpResponseMessage((System.Net.HttpStatusCode)result.Status) { Content = new StringContent(result.Body) };
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/" + path);
        request.Headers.Add("X-FitnessApp-Session", "1");
        request.Headers.Add("Origin", new Uri(navigationManager.BaseUri).GetLeftPart(UriPartial.Authority));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await httpClientFactory.CreateClient("FitnessAppSession").SendAsync(request, cancellationToken);
    }

    private sealed record SessionHttpResult(int Status, string Body);

    public async Task<CurrentUserResult> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var generation = authenticationState.Generation;
        try
        {
            using var response = await Client.GetAsync("api/auth/me", cancellationToken);
            if (generation != authenticationState.Generation) return CurrentUserResult.Unavailable();
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            {
                return CurrentUserResult.Unauthorized();
            }

            if (!response.IsSuccessStatusCode)
            {
                return CurrentUserResult.Unavailable();
            }

            var user = await response.Content.ReadFromJsonAsync<AuthenticatedUserResponse>(cancellationToken);
            if (user is null)
            {
                return CurrentUserResult.Unavailable();
            }

            if (generation != authenticationState.Generation) return CurrentUserResult.Unavailable();
            authenticationState.SetUser(user);
            return CurrentUserResult.Succeeded(user);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return CurrentUserResult.Unavailable();
        }
    }

    public async Task<LogoutStatus> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var status = LogoutStatus.ServerLogoutUnconfirmed;
        LogoutPending = true;
        authenticationState.BeginTransition();
        IsUnavailable = false;

        try
        {
            if (javaScript is not null)
            {
                try { await javaScript.InvokeVoidAsync("fitnessSession.suppress", cancellationToken); }
                catch (JSException) { /* Always attempt server revocation, even when browser storage fails. */ }
                try { await javaScript.InvokeVoidAsync("fitnessPush.clear", cancellationToken); }
                catch (JSException) { /* Server revocation must still be attempted. */ }
            }
            using var response = await SessionRequestAsync("logout", null, cancellationToken);
            status = response.IsSuccessStatusCode
                ? LogoutStatus.Succeeded
                : response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    ? LogoutStatus.SessionRejected
                    : LogoutStatus.ServerLogoutUnconfirmed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JSException)
        {
            status = LogoutStatus.ServerLogoutUnconfirmed;
        }
        finally
        {
            authenticationState.ClearSession();
            LogoutPending = status == LogoutStatus.ServerLogoutUnconfirmed;
            StateChanged?.Invoke();
            var destination = status is LogoutStatus.ServerLogoutUnconfirmed
                ? "/login?logout=unconfirmed"
                : "/login";
            navigationManager.NavigateTo(destination, replace: true);
        }

        return status;
    }

    private HttpClient Client => httpClientFactory.CreateClient(ClientName);
}

public enum CurrentUserStatus
{
    Succeeded,
    Unauthorized,
    Unavailable
}

public sealed record CurrentUserResult(CurrentUserStatus Status, AuthenticatedUserResponse? User)
{
    public static CurrentUserResult Succeeded(AuthenticatedUserResponse user) =>
        new(CurrentUserStatus.Succeeded, user);

    public static CurrentUserResult Unauthorized() =>
        new(CurrentUserStatus.Unauthorized, null);

    public static CurrentUserResult Unavailable() =>
        new(CurrentUserStatus.Unavailable, null);
}

public enum LogoutStatus
{
    Succeeded,
    SessionRejected,
    ServerLogoutUnconfirmed
}
