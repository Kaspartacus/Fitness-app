using FitnessApp.Application.Authentication;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Server.Authentication;

internal static class AdminBootstrapCommand
{
    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var email = app.Configuration["BootstrapAdmin:Email"];
        var password = app.Configuration["BootstrapAdmin:Password"];
        var displayName = app.Configuration["BootstrapAdmin:DisplayName"] ?? "Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                "Administrator bootstrap requires BootstrapAdmin:Email and BootstrapAdmin:Password in local secret configuration.");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        var result = await bootstrapper.BootstrapAsync(
            email.Trim(),
            password,
            displayName.Trim(),
            cancellationToken);

        switch (result.Status)
        {
            case AdminBootstrapStatus.Created:
                Console.WriteLine("Administrator bootstrap completed.");
                return 0;
            case AdminBootstrapStatus.AlreadyInitialized:
                Console.WriteLine("Administrator bootstrap has already been completed; no changes were made.");
                return 0;
            case AdminBootstrapStatus.ExistingAccountCannotBeElevated:
                Console.Error.WriteLine("Bootstrap refused to elevate an existing account.");
                return 1;
            default:
                Console.Error.WriteLine("Administrator bootstrap failed:");
                foreach (var error in result.Errors)
                {
                    Console.Error.WriteLine($"- {error}");
                }

                return 1;
        }
    }
}
