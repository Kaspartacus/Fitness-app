using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FitnessApp.IntegrationTests;

internal sealed class AuthWebApplicationFactory(
    int loginPermitLimit = 10,
    int registrationPermitLimit = 5)
    : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"fitnessapp-tests-{Guid.NewGuid():N}.db");

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string ValidPassword { get; } = $"T!{Guid.NewGuid():N}aA1";

    public string Issuer => "FitnessApp.Tests";

    public string Audience => "FitnessApp.Tests.Client";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                ["Authentication:Jwt:Issuer"] = Issuer,
                ["Authentication:Jwt:Audience"] = Audience,
                ["Authentication:Jwt:SigningKey"] = SigningKey,
                ["Authentication:Jwt:AccessTokenMinutes"] = "15",
                ["Authentication:Jwt:ClockSkewSeconds"] = "0",
                ["Authentication:LoginRateLimit:PermitLimit"] = loginPermitLimit.ToString(),
                ["Authentication:LoginRateLimit:WindowSeconds"] = "60",
                ["Authentication:RegistrationRateLimit:PermitLimit"] = registrationPermitLimit.ToString(),
                ["Authentication:RegistrationRateLimit:WindowSeconds"] = "60",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Testing:EnableTestEndpoints"] = "true"
            });
        });
        builder.ConfigureServices(services =>
            services.AddDbContext<FitnessDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}")));
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await dbContext.Database.MigrateAsync();

        foreach (var roleName in new[] { AuthenticationConstants.AdminRole, AuthenticationConstants.UserRole })
        {
            var normalizedRoleName = roleName.ToUpperInvariant();
            if (!await dbContext.Roles.AnyAsync(role => role.NormalizedName == normalizedRoleName))
            {
                dbContext.Roles.Add(new IdentityRole
                {
                    Name = roleName,
                    NormalizedName = normalizedRoleName
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task<ApplicationUser> CreateUserAsync(
        AccountApprovalStatus approvalStatus,
        string role = AuthenticationConstants.UserRole,
        string? email = null,
        DateTimeOffset? registeredAt = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userEmail = email ?? $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = userEmail,
            Email = userEmail,
            EmailConfirmed = true,
            DisplayName = "Testbruger",
            ApprovalStatus = approvalStatus,
            RegisteredAt = registeredAt?.UtcDateTime
        };

        var creation = await userManager.CreateAsync(user, ValidPassword);
        Assert.True(creation.Succeeded, string.Join(", ", creation.Errors.Select(error => error.Description)));
        var assignment = await userManager.AddToRoleAsync(user, role);
        Assert.True(assignment.Succeeded, string.Join(", ", assignment.Errors.Select(error => error.Description)));
        return user;
    }

    public async Task SetApprovalStatusAsync(string userId, AccountApprovalStatus approvalStatus)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId);
        user.ApprovalStatus = approvalStatus;
        await dbContext.SaveChangesAsync();
    }

    public async Task RemoveRoleAsync(string userId, string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        Assert.NotNull(user);

        var removal = await userManager.RemoveFromRoleAsync(user, role);
        Assert.True(removal.Succeeded, string.Join(", ", removal.Errors.Select(error => error.Description)));
    }

    public async Task<string> CreateSessionAsync(ApplicationUser user, DateTimeOffset expiresAt)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var sessionId = Guid.NewGuid().ToString("N");
        dbContext.UserSessions.Add(new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt
        });
        await dbContext.SaveChangesAsync();
        return sessionId;
    }

    public string CreateToken(
        ApplicationUser user,
        string sessionId,
        DateTimeOffset expiresAt,
        string? issuer = null,
        string? audience = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(SigningKey)),
            algorithm);
        var token = new JwtSecurityToken(
            issuer ?? Issuer,
            audience ?? Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Sid, sessionId),
                new Claim(ClaimTypes.Role, AuthenticationConstants.UserRole)
            ],
            DateTime.UtcNow.AddMinutes(-2),
            expiresAt.UtcDateTime,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
