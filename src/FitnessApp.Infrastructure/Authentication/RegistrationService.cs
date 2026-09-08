using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Authentication;

internal sealed class RegistrationService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    FitnessDbContext dbContext,
    TimeProvider timeProvider) : IRegistrationService
{
    public async Task<RegistrationResult> RegisterAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return AcceptedExisting();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await roleManager.RoleExistsAsync(AuthenticationConstants.UserRole))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(AuthenticationConstants.UserRole));
                if (!roleResult.Succeeded &&
                    !await roleManager.RoleExistsAsync(AuthenticationConstants.UserRole))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Invalid("Registreringen kunne ikke gennemføres. Prøv igen.");
                }
            }

            var user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = false,
                DisplayName = displayName.Trim(),
                ApprovalStatus = AccountApprovalStatus.Pending,
                RegisteredAt = timeProvider.GetUtcNow().UtcDateTime
            };

            var creation = await userManager.CreateAsync(user, password);
            if (!creation.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                if (creation.Errors.Any(IsDuplicate))
                {
                    return AcceptedExisting();
                }

                return new RegistrationResult(
                    RegistrationStatus.Invalid,
                    creation.Errors.Select(ToDanishMessage).Distinct().ToArray());
            }

            var roleAssignment = await userManager.AddToRoleAsync(user, AuthenticationConstants.UserRole);
            if (!roleAssignment.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Invalid("Registreringen kunne ikke gennemføres. Prøv igen.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new RegistrationResult(RegistrationStatus.Created, []);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
            {
                return AcceptedExisting();
            }

            throw;
        }
    }

    private static bool IsDuplicate(IdentityError error) =>
        error.Code is "DuplicateEmail" or "DuplicateUserName";

    private static string ToDanishMessage(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Adgangskoden er for kort.",
        "PasswordRequiresDigit" => "Adgangskoden skal indeholde et tal.",
        "PasswordRequiresLower" => "Adgangskoden skal indeholde et lille bogstav.",
        "PasswordRequiresUpper" => "Adgangskoden skal indeholde et stort bogstav.",
        "PasswordRequiresNonAlphanumeric" => "Adgangskoden skal indeholde et specialtegn.",
        "InvalidEmail" or "InvalidUserName" => "Indtast en gyldig e-mailadresse.",
        _ => "Registreringen kunne ikke gennemføres. Kontrollér oplysningerne og prøv igen."
    };

    private static RegistrationResult AcceptedExisting() =>
        new(RegistrationStatus.ExistingAccountAccepted, []);

    private static RegistrationResult Invalid(string error) =>
        new(RegistrationStatus.Invalid, [error]);
}
