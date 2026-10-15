using System.Security.Claims;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace FitnessApp.Client.Authentication;

public sealed class InMemoryAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private AuthenticationState authenticationState = Anonymous;

    public string? AccessToken { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public string? SessionId { get; private set; }
    public long Generation { get; private set; }
    public long TransitionGeneration { get; private set; }
    public void BeginTransition()
    {
        TransitionGeneration++;
        ClearSession();
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(authenticationState);

    public void SetSession(LoginResponse response)
    {
        AccessToken = response.AccessToken;
        ExpiresAt = response.ExpiresAt;
        SessionId = response.SessionId;
        SetUser(response.User);
    }

    public void SetUser(AuthenticatedUserResponse user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        authenticationState = new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        NotifyAuthenticationStateChanged(Task.FromResult(authenticationState));
    }

    public void SuspendSession() => ClearSession(preserveSessionIdentity: true);

    public void ClearSession()
    {
        // Definitive rejection/logout also invalidates any delayed successful response.
        Generation++;
        ClearSession(preserveSessionIdentity: false);
    }

    private void ClearSession(bool preserveSessionIdentity)
    {
        AccessToken = null;
        if (!preserveSessionIdentity) SessionId = null;
        ExpiresAt = default;
        authenticationState = Anonymous;
        NotifyAuthenticationStateChanged(Task.FromResult(authenticationState));
    }
}
