using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components;

namespace FitnessApp.Client.Authentication;

public sealed class AuthenticationClient(
    IHttpClientFactory httpClientFactory,
    InMemoryAuthenticationStateProvider authenticationState,
    NavigationManager navigationManager)
{
    public const string ClientName = "FitnessAppApi";
    private const string GenericLoginFailure =
        "E-mail eller adgangskode er forkert, eller kontoen har ikke adgang.";

    public async Task<string?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsJsonAsync("api/auth/login", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                ? "For mange loginforsøg. Prøv igen senere."
                : GenericLoginFailure;
        }

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The login response was empty.");
        authenticationState.SetSession(login);
        return null;
    }

    public async Task<CurrentUserResult> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync("api/auth/me", cancellationToken);
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

        try
        {
            using var response = await Client.PostAsync("api/auth/logout", content: null, cancellationToken);
            status = response.IsSuccessStatusCode
                ? LogoutStatus.Succeeded
                : response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    ? LogoutStatus.SessionRejected
                    : LogoutStatus.ServerLogoutUnconfirmed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            status = LogoutStatus.ServerLogoutUnconfirmed;
        }
        finally
        {
            authenticationState.ClearSession();
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
