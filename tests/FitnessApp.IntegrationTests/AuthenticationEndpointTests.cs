using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Domain.Users;
using Microsoft.IdentityModel.Tokens;

namespace FitnessApp.IntegrationTests;

public sealed class AuthenticationEndpointTests
{
    [Fact]
    public async Task ApprovedAdministratorCanLogin()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        using var client = factory.CreateHttpsClient();

        var response = await LoginAsync(client, admin.Email!, factory.ValidPassword);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.Contains(AuthenticationConstants.AdminRole, login.User.Roles);
    }

    [Fact]
    public async Task InvalidCredentialsReturnTheSameGenericFailure()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        var existingUserResponse = await LoginAsync(client, user.Email!, $"{factory.ValidPassword}wrong");
        var unknownUserResponse = await LoginAsync(client, "unknown@example.test", factory.ValidPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, existingUserResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUserResponse.StatusCode);
        Assert.Equal(
            await existingUserResponse.Content.ReadAsStringAsync(),
            await unknownUserResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(AccountApprovalStatus.Pending)]
    [InlineData(AccountApprovalStatus.Rejected)]
    public async Task UnapprovedUsersCannotLogin(AccountApprovalStatus status)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(status);
        using var client = factory.CreateHttpsClient();

        var response = await LoginAsync(client, user.Email!, factory.ValidPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTokensAreRejected()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var sessionId = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddMinutes(30));
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/auth/me")).StatusCode);

        var expired = factory.CreateToken(user, sessionId, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, expired)).StatusCode);

        var valid = factory.CreateToken(user, sessionId, DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(HttpStatusCode.OK, (await GetCurrentUserAsync(client, valid)).StatusCode);

        var tokenSegments = valid.Split('.');
        tokenSegments[2] = $"{(tokenSegments[2][0] == 'a' ? 'b' : 'a')}{tokenSegments[2][1..]}";
        var tampered = string.Join('.', tokenSegments);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, tampered)).StatusCode);

        var wrongIssuer = factory.CreateToken(
            user,
            sessionId,
            DateTimeOffset.UtcNow.AddMinutes(10),
            issuer: "WrongIssuer");
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, wrongIssuer)).StatusCode);

        var wrongAudience = factory.CreateToken(
            user,
            sessionId,
            DateTimeOffset.UtcNow.AddMinutes(10),
            audience: "WrongAudience");
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, wrongAudience)).StatusCode);

        var wrongAlgorithm = factory.CreateToken(
            user,
            sessionId,
            DateTimeOffset.UtcNow.AddMinutes(10),
            algorithm: SecurityAlgorithms.HmacSha384);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, wrongAlgorithm)).StatusCode);
    }

    [Fact]
    public async Task ApprovedSessionCanAccessProtectedEndpoint()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var token = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);

        var response = await GetCurrentUserAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonAdministratorIsForbiddenFromAdminPolicy()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var token = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("api/test/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokedAdministratorRoleTakesEffectForTheExistingToken()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var administrator = await factory.CreateUserAsync(
            AccountApprovalStatus.Approved,
            AuthenticationConstants.AdminRole);
        using var client = factory.CreateHttpsClient();
        var token = await LoginAndGetTokenAsync(client, administrator.Email!, factory.ValidPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/test/admin")).StatusCode);

        await factory.RemoveRoleAsync(administrator.Id, AuthenticationConstants.AdminRole);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/test/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LogoutInvalidatesCurrentTokenImmediately()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var token = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var logout = await client.PostAsync("api/auth/logout", content: null);
        var currentUser = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, currentUser.StatusCode);
    }

    [Fact]
    public async Task ApprovalRevocationInvalidatesExistingToken()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var token = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);

        await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Rejected);
        var currentUser = await GetCurrentUserAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, currentUser.StatusCode);
    }

    [Fact]
    public async Task RepeatedInvalidPasswordsLockTheAccount()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(client, user.Email!, $"{factory.ValidPassword}wrong");
        }

        var response = await LoginAsync(client, user.Email!, factory.ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginEndpointIsRateLimited()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await LoginAsync(client, "unknown@example.test", factory.ValidPassword);
        }

        var response = await LoginAsync(client, "unknown@example.test", factory.ValidPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRouteDoesNotFallBackToClientApplication()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("api/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = email, Password = password });

    private static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email, string password)
    {
        var response = await LoginAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private static Task<HttpResponseMessage> GetCurrentUserAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}
