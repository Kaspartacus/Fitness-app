using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Authentication;

internal sealed class UserAdministrationService(
    FitnessDbContext dbContext,
    TimeProvider timeProvider) : IUserAdministrationService
{
    public async Task<IReadOnlyCollection<PendingRegistration>> GetPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var registrations = await Registrations()
            .OrderBy(user => user.RegisteredAt)
            .Take(limit)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                Email = user.Email!,
                RegisteredAt = user.RegisteredAt!.Value
            })
            .ToArrayAsync(cancellationToken);

        return registrations
            .Select(user => new PendingRegistration(
                user.Id,
                user.DisplayName,
                user.Email,
                new DateTimeOffset(DateTime.SpecifyKind(user.RegisteredAt, DateTimeKind.Utc))))
            .ToArray();
    }

    public async Task<RegistrationDecisionResult> DecideAsync(
        string registrationId,
        AccountApprovalStatus decision,
        string administratorId,
        CancellationToken cancellationToken)
    {
        if (decision is not AccountApprovalStatus.Approved and not AccountApprovalStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        var decidedAt = timeProvider.GetUtcNow().UtcDateTime;
        var affected = await Registrations()
            .Where(user => user.Id == registrationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.ApprovalStatus, decision)
                    .SetProperty(user => user.DecidedAt, decidedAt)
                    .SetProperty(user => user.DecidedByUserId, administratorId),
                cancellationToken);

        if (affected == 1)
        {
            return new RegistrationDecisionResult(RegistrationDecisionStatus.Completed, decision);
        }

        var currentStatus = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == registrationId && user.RegisteredAt != null)
            .Select(user => (AccountApprovalStatus?)user.ApprovalStatus)
            .SingleOrDefaultAsync(cancellationToken);

        return currentStatus is null
            ? new RegistrationDecisionResult(RegistrationDecisionStatus.NotFound)
            : new RegistrationDecisionResult(RegistrationDecisionStatus.AlreadyProcessed, currentStatus);
    }

    private IQueryable<ApplicationUser> Registrations()
    {
        var normalizedUserRole = AuthenticationConstants.UserRole.ToUpperInvariant();
        var normalizedAdminRole = AuthenticationConstants.AdminRole.ToUpperInvariant();

        return dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.RegisteredAt != null &&
                user.ApprovalStatus == AccountApprovalStatus.Pending &&
                dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id &&
                    dbContext.Roles.Any(role =>
                        role.Id == userRole.RoleId &&
                        role.NormalizedName == normalizedUserRole)) &&
                !dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id &&
                    dbContext.Roles.Any(role =>
                        role.Id == userRole.RoleId &&
                        role.NormalizedName == normalizedAdminRole)));
    }
}
