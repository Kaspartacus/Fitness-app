using FitnessApp.Domain.Users;

namespace FitnessApp.Application.Authentication;

public enum RegistrationStatus
{
    Created,
    ExistingAccountAccepted,
    Invalid
}

public sealed record RegistrationResult(
    RegistrationStatus Status,
    IReadOnlyCollection<string> Errors);

public sealed record PendingRegistration(
    string Id,
    string DisplayName,
    string Email,
    DateTimeOffset RegisteredAt);

public enum RegistrationDecisionStatus
{
    Completed,
    AlreadyProcessed,
    NotFound
}

public sealed record RegistrationDecisionResult(
    RegistrationDecisionStatus Status,
    AccountApprovalStatus? CurrentStatus = null);
