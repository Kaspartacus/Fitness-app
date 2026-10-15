namespace FitnessApp.Contracts.Authentication;

public sealed record AuthenticatedUserResponse(
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserResponse User,
    string? SessionId = null);

public sealed record RefreshRequest(bool UserActivity = false, string? ExpectedSessionId = null);
