using System.Security.Claims;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace FitnessApp.Client.Authentication;

public sealed class InMemoryAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private AuthenticationState authenticationState = Anonymous;

    public string? AccessToken { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(authenticationState);

    public void SetSession(LoginResponse response)
    {
        AccessToken = response.AccessToken;
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

    public void ClearSession()
    {
        AccessToken = null;
        authenticationState = Anonymous;
        NotifyAuthenticationStateChanged(Task.FromResult(authenticationState));
    }
}
