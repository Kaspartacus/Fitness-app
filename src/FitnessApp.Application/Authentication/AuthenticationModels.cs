namespace FitnessApp.Application.Authentication;

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    NotApproved
}

public sealed record AuthenticatedUser(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles);

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record LoginResult(
    LoginStatus Status,
    IssuedAccessToken? AccessToken = null,
    AuthenticatedUser? User = null,
    RefreshCredential? Refresh = null,
    string? SessionId = null);

public enum AdminBootstrapStatus
{
    Created,
    AlreadyInitialized,
    ExistingAccountCannotBeElevated,
    Failed
}

public sealed record AdminBootstrapResult(
    AdminBootstrapStatus Status,
    IReadOnlyCollection<string> Errors);

public sealed record RefreshCredential(string Value, DateTimeOffset ExpiresAt);

public enum RefreshStatus { Succeeded, Rejected, Conflict }

public sealed record RefreshResult(
    RefreshStatus Status,
    IssuedAccessToken? AccessToken = null,
    AuthenticatedUser? User = null,
    RefreshCredential? Refresh = null,
    string? SessionId = null);
