namespace FitnessApp.Contracts.Authentication;

public sealed record PendingRegistrationResponse(
    string Id,
    string DisplayName,
    string Email,
    DateTimeOffset RegisteredAt);

public sealed record PendingRegistrationListResponse(
    IReadOnlyCollection<PendingRegistrationResponse> Registrations,
    int Limit);
