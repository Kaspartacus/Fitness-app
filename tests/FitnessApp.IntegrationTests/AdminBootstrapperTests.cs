using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class AdminBootstrapperTests
{
    [Fact]
    public async Task BootstrapDoesNotElevateOrOverwriteExistingAccount()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var existing = await factory.CreateUserAsync(AccountApprovalStatus.Pending);
        var originalPasswordHash = existing.PasswordHash;

        await using var scope = factory.Services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        var result = await bootstrapper.BootstrapAsync(
            existing.Email!,
            $"{factory.ValidPassword}xA1!",
            "Changed name",
            CancellationToken.None);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var unchanged = await userManager.FindByIdAsync(existing.Id);

        Assert.Equal(AdminBootstrapStatus.ExistingAccountCannotBeElevated, result.Status);
        Assert.Equal(AccountApprovalStatus.Pending, unchanged!.ApprovalStatus);
        Assert.Equal("Testbruger", unchanged.DisplayName);
        Assert.Equal(originalPasswordHash, unchanged.PasswordHash);
        Assert.False(await userManager.IsInRoleAsync(unchanged, AuthenticationConstants.AdminRole));
    }

    [Fact]
    public async Task BootstrapIsSafeToRerunAfterAdministratorCreation()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        var first = await bootstrapper.BootstrapAsync(
            "admin@example.test",
            factory.ValidPassword,
            "Administrator",
            CancellationToken.None);
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var initiallyCreatedAdmin = await dbContext.Users.SingleAsync();
        var originalPasswordHash = initiallyCreatedAdmin.PasswordHash;

        var second = await bootstrapper.BootstrapAsync(
            "other@example.test",
            $"{factory.ValidPassword}xA1!",
            "Other",
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var createdAdmin = await dbContext.Users.SingleAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal(AdminBootstrapStatus.Created, first.Status);
        Assert.Equal(AdminBootstrapStatus.AlreadyInitialized, second.Status);
        Assert.Equal(1, await dbContext.Users.CountAsync());
        Assert.Equal(AccountApprovalStatus.Approved, createdAdmin.ApprovalStatus);
        Assert.Equal("Administrator", createdAdmin.DisplayName);
        Assert.Equal(originalPasswordHash, createdAdmin.PasswordHash);
        Assert.True(await userManager.IsInRoleAsync(createdAdmin, AuthenticationConstants.AdminRole));
    }
}
