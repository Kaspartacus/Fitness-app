using FitnessApp.Domain.Users;

namespace FitnessApp.Application.Authentication;

public interface IRegistrationService
{
    Task<RegistrationResult> RegisterAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken);
}

public interface IUserAdministrationService
{
    Task<IReadOnlyCollection<PendingRegistration>> GetPendingAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<RegistrationDecisionResult> DecideAsync(
        string registrationId,
        AccountApprovalStatus decision,
        string administratorId,
        CancellationToken cancellationToken);
}
