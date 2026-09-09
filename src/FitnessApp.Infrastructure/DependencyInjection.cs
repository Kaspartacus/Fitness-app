using FitnessApp.Application.Authentication;
using FitnessApp.Application.Email;
using FitnessApp.Infrastructure.Authentication;
using FitnessApp.Infrastructure.Email;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        TimeSpan passwordResetTokenLifetime)
    {
        services.AddDbContext<FitnessDbContext>(options => options.UseSqlite(connectionString));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.User.RequireUniqueEmail = true;
                options.Tokens.PasswordResetTokenProvider = PasswordResetConstants.TokenProviderName;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<FitnessDbContext>()
            .AddTokenProvider<PasswordResetTokenProvider<ApplicationUser>>(
                PasswordResetConstants.TokenProviderName);

        services.Configure<PasswordResetTokenProviderOptions>(options =>
            options.TokenLifespan = passwordResetTokenLifetime);

        services.AddScoped<FitnessApp.Application.Strength.IStrengthProgramService, FitnessApp.Infrastructure.Strength.StrengthProgramService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        return services;
    }

    public static IServiceCollection AddEmailDelivery(
        this IServiceCollection services,
        bool usePickupDirectory)
    {
        services.AddSingleton<BoundedEmailQueue>();
        services.AddSingleton<IEmailQueue>(provider => provider.GetRequiredService<BoundedEmailQueue>());
        services.AddSingleton<IEmailSender>(provider => usePickupDirectory
            ? ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(provider)
            : ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider));
        services.AddHostedService<EmailDeliveryWorker>();
        return services;
    }
}
