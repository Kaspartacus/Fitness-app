namespace FitnessApp.Contracts.Authentication;

public sealed record AuthenticatedUserResponse(
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserResponse User);
