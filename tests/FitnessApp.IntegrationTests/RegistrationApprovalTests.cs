using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class RegistrationApprovalTests
{
    [Fact]
    public async Task ValidRegistrationCreatesOnlyPendingUserWithoutSession()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();

        var response = await RegisterAsync(client, "Ny Bruger", email, factory.ValidPassword);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant());
        Assert.Equal("Ny Bruger", user.DisplayName);
        Assert.Equal(AccountApprovalStatus.Pending, user.ApprovalStatus);
        Assert.False(user.EmailConfirmed);
        Assert.NotNull(user.RegisteredAt);
        Assert.Null(user.DecidedAt);
        Assert.Null(user.DecidedByUserId);
        Assert.Equal([AuthenticationConstants.UserRole], await userManager.GetRolesAsync(user));
        Assert.Empty(await dbContext.UserSessions.ToArrayAsync());
    }

    [Fact]
    public async Task InvalidRegistrationDoesNotCreateAccount()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await RegisterAsync(client, "X", "not-an-email", "weak");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        Assert.Empty(await dbContext.Users.ToArrayAsync());
    }

    [Fact]
    public async Task DuplicateRegistrationIsNeutralAndDoesNotOverwriteAccount()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var existing = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var originalHash = existing.PasswordHash;
        using var client = factory.CreateHttpsClient();

        var response = await RegisterAsync(client, "Forsøgt ændring", existing.Email!, $"{factory.ValidPassword}X1!");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var unchanged = await dbContext.Users.SingleAsync();
        Assert.Equal("Testbruger", unchanged.DisplayName);
        Assert.Equal(AccountApprovalStatus.Approved, unchanged.ApprovalStatus);
        Assert.Equal(originalHash, unchanged.PasswordHash);
        Assert.Null(unchanged.RegisteredAt);
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistrationCreatesExactlyOneAccount()
    {
        using var factory = new AuthWebApplicationFactory(registrationPermitLimit: 10);
        await factory.InitializeDatabaseAsync();
        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();
        var email = NewEmail();

        var responses = await Task.WhenAll(
            RegisterAsync(firstClient, "Første navn", email, factory.ValidPassword),
            RegisterAsync(secondClient, "Andet navn", email, factory.ValidPassword));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        Assert.Equal(1, await dbContext.Users.CountAsync());
        var user = await dbContext.Users.SingleAsync();
        Assert.Equal(AccountApprovalStatus.Pending, user.ApprovalStatus);
    }

    [Fact]
    public async Task PrivilegedInputIsIgnoredDuringRegistration()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("api/registrations", new
        {
            displayName = "Almindelig bruger",
            email,
            password = factory.ValidPassword,
            confirmPassword = factory.ValidPassword,
            approvalStatus = "Approved",
            roles = new[] { AuthenticationConstants.AdminRole },
            emailConfirmed = true,
            decidedByUserId = "attacker"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await dbContext.Users.SingleAsync();
        Assert.Equal(AccountApprovalStatus.Pending, user.ApprovalStatus);
        Assert.False(user.EmailConfirmed);
        Assert.Null(user.DecidedByUserId);
        Assert.Equal([AuthenticationConstants.UserRole], await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task RegistrationEndpointIsRateLimited()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(client, "Bruger et", NewEmail(), factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(client, "Bruger to", NewEmail(), factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(client, "Bruger tre", NewEmail(), factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(client, "Bruger fire", NewEmail(), factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(client, "Bruger fem", NewEmail(), factory.ValidPassword)).StatusCode);
        var limited = await RegisterAsync(client, "Bruger seks", NewEmail(), factory.ValidPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("no-store", limited.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AdministrationEndpointsRequireCurrentAdministratorRole()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("api/admin/registrations/pending")).StatusCode);

        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("api/admin/registrations/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"api/admin/registrations/{user.Id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task PendingListIsBoundedAndContainsOnlyEligibleRegistrations()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        var oldest = await factory.CreateUserAsync(
            AccountApprovalStatus.Pending,
            email: NewEmail(),
            registeredAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        await factory.CreateUserAsync(
            AccountApprovalStatus.Pending,
            email: NewEmail(),
            registeredAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await factory.CreateUserAsync(AccountApprovalStatus.Pending);
        await factory.CreateUserAsync(
            AccountApprovalStatus.Rejected,
            email: NewEmail(),
            registeredAt: DateTimeOffset.UtcNow.AddMinutes(-4));
        await factory.CreateUserAsync(
            AccountApprovalStatus.Pending,
            AuthenticationConstants.AdminRole,
            NewEmail(),
            DateTimeOffset.UtcNow.AddMinutes(-3));
        using var client = factory.CreateHttpsClient();
        await AuthenticateAsync(client, administrator, factory.ValidPassword);

        var response = await client.GetAsync("api/admin/registrations/pending?limit=1");
        var result = await response.Content.ReadFromJsonAsync<PendingRegistrationListResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.Limit);
        var listed = Assert.Single(result.Registrations);
        Assert.Equal(oldest.Id, listed.Id);
        Assert.Equal(oldest.Email, listed.Email);
        Assert.NotEqual(default, listed.RegisteredAt);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("api/admin/registrations/pending?limit=101")).StatusCode);
    }

    [Fact]
    public async Task ApprovalRecordsDecisionAndEnablesLogin()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        using var registrationClient = factory.CreateHttpsClient();
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.Accepted,
            (await RegisterAsync(registrationClient, "Godkend mig", email, factory.ValidPassword)).StatusCode);
        var registered = await FindByEmailAsync(factory, email);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await LoginAsync(registrationClient, email, factory.ValidPassword)).StatusCode);
        using var adminClient = factory.CreateHttpsClient();
        await AuthenticateAsync(adminClient, administrator, factory.ValidPassword);
        var before = DateTimeOffset.UtcNow;

        var decision = await adminClient.PostAsync($"api/admin/registrations/{registered.Id}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);
        var approved = await FindByEmailAsync(factory, email);
        Assert.Equal(AccountApprovalStatus.Approved, approved.ApprovalStatus);
        Assert.Equal(administrator.Id, approved.DecidedByUserId);
        Assert.InRange(
            new DateTimeOffset(DateTime.SpecifyKind(approved.DecidedAt!.Value, DateTimeKind.Utc)),
            before,
            DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(HttpStatusCode.OK,
            (await LoginAsync(registrationClient, email, factory.ValidPassword)).StatusCode);
    }

    [Fact]
    public async Task RejectionRecordsDecisionAndKeepsLoginBlocked()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        using var client = factory.CreateHttpsClient();
        var email = NewEmail();
        await RegisterAsync(client, "Afvis mig", email, factory.ValidPassword);
        var registered = await FindByEmailAsync(factory, email);
        await AuthenticateAsync(client, administrator, factory.ValidPassword);

        var decision = await client.PostAsync($"api/admin/registrations/{registered.Id}/reject", null);

        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);
        var rejected = await FindByEmailAsync(factory, email);
        Assert.Equal(AccountApprovalStatus.Rejected, rejected.ApprovalStatus);
        Assert.Equal(administrator.Id, rejected.DecidedByUserId);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await LoginAsync(client, email, factory.ValidPassword)).StatusCode);
    }

    [Fact]
    public async Task RepeatedDecisionIsConflictAndDoesNotOverwriteAuditMetadata()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        var registration = await factory.CreateUserAsync(
            AccountApprovalStatus.Pending,
            email: NewEmail(),
            registeredAt: DateTimeOffset.UtcNow);
        using var client = factory.CreateHttpsClient();
        await AuthenticateAsync(client, administrator, factory.ValidPassword);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsync($"api/admin/registrations/{registration.Id}/approve", null)).StatusCode);
        var firstDecision = await FindByEmailAsync(factory, registration.Email!);

        var repeated = await client.PostAsync($"api/admin/registrations/{registration.Id}/reject", null);
        var unchanged = await FindByEmailAsync(factory, registration.Email!);

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(AccountApprovalStatus.Approved, unchanged.ApprovalStatus);
        Assert.Equal(firstDecision.DecidedAt, unchanged.DecidedAt);
        Assert.Equal(firstDecision.DecidedByUserId, unchanged.DecidedByUserId);
    }

    [Fact]
    public async Task ConcurrentOpposingDecisionsProduceOneWinner()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        var registration = await factory.CreateUserAsync(
            AccountApprovalStatus.Pending,
            email: NewEmail(),
            registeredAt: DateTimeOffset.UtcNow);
        using var approveClient = factory.CreateHttpsClient();
        using var rejectClient = factory.CreateHttpsClient();
        await AuthenticateAsync(approveClient, administrator, factory.ValidPassword);
        await AuthenticateAsync(rejectClient, administrator, factory.ValidPassword);

        var responses = await Task.WhenAll(
            approveClient.PostAsync($"api/admin/registrations/{registration.Id}/approve", null),
            rejectClient.PostAsync($"api/admin/registrations/{registration.Id}/reject", null));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var decided = await FindByEmailAsync(factory, registration.Email!);
        var expected = responses[0].StatusCode == HttpStatusCode.NoContent
            ? AccountApprovalStatus.Approved
            : AccountApprovalStatus.Rejected;
        Assert.Equal(expected, decided.ApprovalStatus);
        Assert.Equal(administrator.Id, decided.DecidedByUserId);
        Assert.NotNull(decided.DecidedAt);
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string displayName,
        string email,
        string password) =>
        client.PostAsJsonAsync("api/registrations", new RegistrationRequest
        {
            DisplayName = displayName,
            Email = email,
            Password = password,
            ConfirmPassword = password
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = email, Password = password });

    private static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email, string password)
    {
        using var response = await LoginAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private static async Task AuthenticateAsync(HttpClient client, ApplicationUser user, string password) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAndGetTokenAsync(client, user.Email!, password));

    private static async Task<ApplicationUser> FindByEmailAsync(
        AuthWebApplicationFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        return await dbContext.Users.AsNoTracking()
            .SingleAsync(user => user.NormalizedEmail == email.ToUpperInvariant());
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";
}
