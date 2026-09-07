namespace FitnessApp.Application.Authentication;

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthenticatedUser?> ValidateSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken);

    Task<bool> LogoutAsync(string userId, string sessionId, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AuthenticatedUser user, string sessionId);
}

public interface IAdminBootstrapper
{
    Task<AdminBootstrapResult> BootstrapAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken);
}
