using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class PersistentSessionTests
{
    [Fact]
    public async Task NewBrowserContextRestoresWithPersistentCookieAndHashOnlyPersistence()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var loginClient = factory.CreateHttpsClient(handleCookies: false);
        using var login = await LoginAsync(factory, loginClient, user.Email!);
        var cookie = Cookie(login);
        var header = login.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("secure", header.ToLowerInvariant());
        Assert.Contains("httponly", header.ToLowerInvariant());
        Assert.Contains("samesite=strict", header.ToLowerInvariant());
        Assert.Contains("path=/api/auth", header);
        Assert.Contains("expires=", header);
        Assert.DoesNotContain("domain=", header.ToLowerInvariant());
        using var reopened = Device(factory, cookie);
        using var restored = await RefreshAsync(reopened);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.NotEqual(cookie, Cookie(restored));
        var session = await SessionAsync(factory);
        Assert.NotEqual(Cookie(restored).Split('=')[1], session.RefreshTokenHash);
        Assert.Equal(64, session.RefreshTokenHash!.Length);
        var restoredLogin = (await restored.Content.ReadFromJsonAsync<LoginResponse>())!;
        reopened.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restoredLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await reopened.GetAsync("api/auth/me")).StatusCode);
        Assert.Contains("no-store", restored.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ForegroundActivityRollsDeadlineAndBackgroundRefreshDoesNot()
    {
        var clock = new SessionClock();
        using var factory = new AuthWebApplicationFactory(timeProvider: clock);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, client, user.Email!);
        var first = await SessionAsync(factory);
        clock.Advance(TimeSpan.FromDays(29));
        using var background = await RefreshAsync(client);
        Assert.Equal(HttpStatusCode.OK, background.StatusCode);
        Assert.Equal(first.ExpiresAt, (await SessionAsync(factory)).ExpiresAt);
        using var active = await RefreshAsync(client, true);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal(clock.GetUtcNow().AddDays(30), (await SessionAsync(factory)).ExpiresAt);
        var renewed = Cookie(active);
        clock.Advance(TimeSpan.FromDays(29));
        using var again = await RefreshAsync(client, true);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); // No fixed monthly login.
        Assert.NotEqual(renewed, Cookie(again));
        var deadline = (await SessionAsync(factory)).ExpiresAt;
        clock.Advance(TimeSpan.FromSeconds(30));
        using var throttled = await RefreshAsync(client, true);
        Assert.Equal(deadline, (await SessionAsync(factory)).ExpiresAt);
    }

    [Fact]
    public async Task ThirtyDaysWithoutActivityRejectsEvenAnActivityRequestAndAccessJwt()
    {
        var clock = new SessionClock();
        using var factory = new AuthWebApplicationFactory(timeProvider: clock);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, client, user.Email!);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, true)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRotationReturnsIdenticalSuccessorAndAcknowledgedReplayRevokesOnlyThatDevice()
    {
        var clock = new SessionClock();
        using var factory = new AuthWebApplicationFactory(timeProvider: clock);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var first = factory.CreateHttpsClient(handleCookies: false);
        using var second = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, first, user.Email!);
        using var otherLogin = await LoginAsync(factory, second, user.Email!);
        var initialCookie = Cookie(login);
        using var tabA = Device(factory, initialCookie);
        using var tabB = Device(factory, initialCookie);
        var rotations = await Task.WhenAll(RefreshAsync(tabA), RefreshAsync(tabB));
        Assert.All(rotations, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(Cookie(rotations[0]), Cookie(rotations[1]));
        var winner = rotations[0];
        using var winningDevice = Device(factory, Cookie(winner));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(winningDevice)).StatusCode);
        using var concurrentOld = await RefreshAsync(tabA);
        Assert.Equal(HttpStatusCode.Conflict, concurrentOld.StatusCode);
        Assert.False(concurrentOld.Headers.Contains("Set-Cookie"));
        clock.Advance(TimeSpan.FromSeconds(31));
        using var replay = Device(factory, initialCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(replay)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(winningDevice)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(second)).StatusCode);
        foreach (var response in rotations) response.Dispose();
    }

    [Fact]
    public async Task LostRotationResponseCanRecoverAfterHoursWithoutRenewingActivity()
    {
        var clock = new SessionClock();
        using var factory = new AuthWebApplicationFactory(timeProvider: clock);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var browser = factory.CreateHttpsClient(handleCookies: false);
        using var other = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, browser, user.Email!);
        using var otherLogin = await LoginAsync(factory, other, user.Email!);
        var oldCookie = Cookie(login);
        using var lostRequest = Device(factory, oldCookie);
        string lostSuccessor;
        using (var discarded = await RefreshAsync(lostRequest))
        {
            Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
            lostSuccessor = Cookie(discarded); // Observe for assertion; deliberately never install it.
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var sessionId = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.SessionId!;
        var before = await db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        clock.Advance(TimeSpan.FromHours(4));
        using var retry = Device(factory, oldCookie);
        using var recovered = await RefreshAsync(retry, true);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(lostSuccessor, Cookie(recovered));
        var after = await db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.LastActivityAt, after.LastActivityAt);
        Assert.Equal(1, await db.ConsumedRefreshTokens.CountAsync(t => t.SessionId == sessionId));
        // Presenting the child acknowledges receipt; an older hash is now genuine replay.
        using var acknowledged = Device(factory, Cookie(recovered));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(acknowledged)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(retry)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(acknowledged)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(other)).StatusCode);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task CookieMutationsRejectMissingHeaderMissingOriginAndForeignOrigin(string endpoint)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();
        object body = endpoint == "login" ? new LoginRequest { Email = "test@example.test", Password = factory.ValidPassword } : new RefreshRequest();
        client.DefaultRequestHeaders.Remove("X-FitnessApp-Session");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/auth/" + endpoint, body)).StatusCode);
        client.DefaultRequestHeaders.Add("X-FitnessApp-Session", "1");
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/auth/" + endpoint, body)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://fitnessapp.example.test.evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/auth/" + endpoint, body)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://fitnessapp.example.test");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        using var crossSite = await client.PostAsJsonAsync("api/auth/" + endpoint, body);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
        Assert.False(crossSite.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task LogoutWorksAfterJwtExpiryAndDoesNotRevokeOtherDevice()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var first = factory.CreateHttpsClient();
        using var other = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, first, user.Email!);
        using var otherLogin = await LoginAsync(factory, other, user.Email!);
        var old = Cookie(login);
        var sessionId = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.SessionId!;
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(user, sessionId, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("api/auth/logout", null)).StatusCode);
        using var stolen = Device(factory, old);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(stolen)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(other)).StatusCode);
    }

    [Fact]
    public async Task PasswordResetRevokesRefreshCredentialsOnEveryDevice()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var first = factory.CreateHttpsClient();
        using var other = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, first, user.Email!);
        using var otherLogin = await LoginAsync(factory, other, user.Email!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var current = (await manager.FindByIdAsync(user.Id))!;
            var resetToken = await manager.GeneratePasswordResetTokenAsync(current);
            var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(resetToken));
            var reset = await scope.ServiceProvider.GetRequiredService<FitnessApp.Application.Authentication.IPasswordResetService>()
                .ResetAsync(user.Email!, encoded, "Changed!Password123", CancellationToken.None);
            Assert.Equal(FitnessApp.Application.Authentication.PasswordResetStatus.Succeeded, reset.Status);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(other)).StatusCode);
    }

    [Fact]
    public async Task AccountSwitchRevokesPriorSessionAndOldTabCannotRenewNewAccount()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var a = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var b = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        using var first = await LoginAsync(factory, client, a.Email!);
        var previous = (await first.Content.ReadFromJsonAsync<LoginResponse>())!;
        using var switched = await LoginAsync(factory, client, b.Email!);
        var newSession = (await switched.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(true, previous.SessionId))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", previous.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(false, newSession.SessionId))).StatusCode);
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("revocation")]
    [InlineData("stamp")]
    public async Task RefreshRechecksAccountApprovalRevocationAndSecurityStamp(string change)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        using var login = await LoginAsync(factory, client, user.Email!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            if (change == "approval")
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ApprovalStatus, AccountApprovalStatus.Rejected));
            if (change == "stamp")
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, "changed-stamp"));
            if (change == "revocation")
                await db.UserSessions.Where(s => s.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client)).StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(AuthWebApplicationFactory factory, HttpClient client, string email) =>
        client.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = email, Password = factory.ValidPassword });
    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, bool active = false) =>
        client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(active));
    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    private static HttpClient Device(AuthWebApplicationFactory factory, string cookie)
    {
        var client = factory.CreateHttpsClient(handleCookies: false);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
    private static async Task<UserSession> SessionAsync(AuthWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().UserSessions.AsNoTracking().SingleAsync();
    }
    private sealed class SessionClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
