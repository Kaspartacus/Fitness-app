using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Authentication;

internal sealed class AdminBootstrapper(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    FitnessDbContext dbContext) : IAdminBootstrapper
{
    public async Task<AdminBootstrapResult> BootstrapAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        var adminUsers = await roleManager.RoleExistsAsync(AuthenticationConstants.AdminRole)
            ? await userManager.GetUsersInRoleAsync(AuthenticationConstants.AdminRole)
            : [];
        if (adminUsers.Any(user => user.ApprovalStatus is AccountApprovalStatus.Approved))
        {
            return Result(AdminBootstrapStatus.AlreadyInitialized);
        }

        if (adminUsers.Count > 0)
        {
            return Result(
                AdminBootstrapStatus.Failed,
                "An administrator role assignment exists in an invalid approval state.");
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Result(AdminBootstrapStatus.ExistingAccountCannotBeElevated);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var roleName in new[] { AuthenticationConstants.AdminRole, AuthenticationConstants.UserRole })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result(AdminBootstrapStatus.Failed, roleResult.Errors.Select(error => error.Description));
                }
            }
        }

        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = displayName,
            ApprovalStatus = AccountApprovalStatus.Approved,
            EmailConfirmed = true
        };

        var creationResult = await userManager.CreateAsync(user, password);
        if (!creationResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result(AdminBootstrapStatus.Failed, creationResult.Errors.Select(error => error.Description));
        }

        var roleAssignmentResult = await userManager.AddToRoleAsync(user, AuthenticationConstants.AdminRole);
        if (!roleAssignmentResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result(AdminBootstrapStatus.Failed, roleAssignmentResult.Errors.Select(error => error.Description));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result(AdminBootstrapStatus.Created);
    }

    private static AdminBootstrapResult Result(
        AdminBootstrapStatus status,
        IEnumerable<string>? errors = null) =>
        new(status, errors?.ToArray() ?? []);

    private static AdminBootstrapResult Result(
        AdminBootstrapStatus status,
        string error) =>
        new(status, [error]);
}
