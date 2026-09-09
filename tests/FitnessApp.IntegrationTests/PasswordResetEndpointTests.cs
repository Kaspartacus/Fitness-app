using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class PasswordResetEndpointTests
{
    private static readonly string NewPassword = $"New!{Guid.NewGuid():N}aA1";

    [Fact]
    public async Task ApprovedAccountReceivesResetEmailFromTrustedOrigin()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        var response = await RequestResetAsync(client, user.Email!);
        var message = await factory.EmailSender.WaitForMessageAsync();
        var link = ParseResetLink(message);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(user.Email, message.Recipient);
        Assert.Equal(Uri.UriSchemeHttps, link.Uri.Scheme);
        Assert.Equal("fitnessapp.example.test", link.Uri.Host);
        Assert.Equal("/nulstil-adgangskode", link.Uri.AbsolutePath);
        Assert.Equal(user.Email, link.Email);
        Assert.True(
            link.Token.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'),
            "The encoded reset token was not URL-safe.");
        Assert.Contains("1 time", message.PlainTextBody);
    }

    [Fact]
    public async Task PublicResponseIsEquivalentForAllAccountStatesAndOnlyApprovedReceivesEmail()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var approved = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var pending = await factory.CreateUserAsync(AccountApprovalStatus.Pending);
        var rejected = await factory.CreateUserAsync(AccountApprovalStatus.Rejected);
        using var client = factory.CreateHttpsClient();

        var responses = new[]
        {
            await RequestResetAsync(client, approved.Email!),
            await RequestResetAsync(client, pending.Email!),
            await RequestResetAsync(client, rejected.Email!),
            await RequestResetAsync(client, "unknown@example.test")
        };
        await factory.EmailSender.WaitForMessageAsync();

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Single(factory.EmailSender.Messages);
    }

    [Fact]
    public async Task CooldownSuppressesDuplicateEmailWithoutChangingPublicResponse()
    {
        using var factory = new AuthWebApplicationFactory(passwordResetCooldownSeconds: 300);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        var first = await RequestResetAsync(client, user.Email!);
        var second = await RequestResetAsync(client, user.Email!);
        await factory.EmailSender.WaitForMessageAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Single(factory.EmailSender.Messages);
    }

    [Fact]
    public async Task PasswordResetEndpointIsRateLimited()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await RequestResetAsync(client, $"unknown-{attempt}@example.test")).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await RequestResetAsync(client, "rate-limited@example.test")).StatusCode);
    }

    [Fact]
    public async Task DeliveryFailurePreservesNeutralResponseAndUsesBoundedRetries()
    {
        var sender = new RecordingEmailSender(failuresBeforeSuccess: 10);
        using var factory = new AuthWebApplicationFactory(emailSender: sender);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        var response = await RequestResetAsync(client, user.Email!);
        await sender.WaitForAttemptsAsync(2);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, sender.Attempts);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task FullQueueDropsWorkSafelyAndReleasesCooldownReservation()
    {
        var sender = new RecordingEmailSender(blockDeliveries: true);
        using var factory = new AuthWebApplicationFactory(
            emailSender: sender,
            configurationOverrides: new Dictionary<string, string?> { ["Email:QueueCapacity"] = "1" });
        await factory.InitializeDatabaseAsync();
        var firstUser = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var secondUser = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var thirdUser = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        await RequestResetAsync(client, firstUser.Email!);
        await sender.WaitForAttemptsAsync(1);
        await RequestResetAsync(client, secondUser.Email!);
        var droppedResponse = await RequestResetAsync(client, thirdUser.Email!);
        sender.ReleaseDeliveries();
        await sender.WaitForMessageAsync();
        await sender.WaitForAttemptsAsync(2);

        var retriedResponse = await RequestResetAsync(client, thirdUser.Email!);
        await sender.WaitForAttemptsAsync(3);

        Assert.Equal(HttpStatusCode.Accepted, droppedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retriedResponse.StatusCode);
        Assert.Equal(3, sender.Messages.Count);
    }

    [Fact]
    public async Task EligibilityChangeAfterRequestPreventsReset()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());

        await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Rejected);
        var reset = await CompleteResetAsync(client, link.Email, link.Token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(client, user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ValidResetChangesPasswordRevokesEverySessionAndPreservesAccountState()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var firstToken = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);
        var secondToken = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());

        var reset = await CompleteResetAsync(client, link.Email, link.Token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email!, factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email!, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, firstToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentUserAsync(client, secondToken)).StatusCode);
        var staleSessionId = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
        var staleSessionToken = factory.CreateToken(
            user,
            staleSessionId,
            DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await GetCurrentUserAsync(client, staleSessionToken)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persistedUser = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(persistedUser);
        Assert.Equal(AccountApprovalStatus.Approved, persistedUser.ApprovalStatus);
        Assert.Equal([AuthenticationConstants.UserRole], await userManager.GetRolesAsync(persistedUser));
        var dbContext = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        Assert.Equal(
            2,
            await dbContext.UserSessions.CountAsync(session =>
                session.UserId == user.Id && session.RevokedAt != null));
    }

    [Fact]
    public async Task OpeningResetLinkDoesNotChangePasswordOrSessions()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var accessToken = await LoginAndGetTokenAsync(client, user.Email!, factory.ValidPassword);
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());

        var getResponse = await client.GetAsync(link.Uri.PathAndQuery);

        Assert.NotEqual(HttpStatusCode.InternalServerError, getResponse.StatusCode);
        Assert.Equal("no-referrer", getResponse.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", getResponse.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email!, factory.ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetCurrentUserAsync(client, accessToken)).StatusCode);
    }

    [Fact]
    public async Task ReplayAndConcurrentUseCanOnlySucceedOnce()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());

        var concurrent = await Task.WhenAll(
            CompleteResetAsync(client, link.Email, link.Token, NewPassword),
            CompleteResetAsync(client, link.Email, link.Token, NewPassword));

        Assert.Single(concurrent, response => response.StatusCode is HttpStatusCode.NoContent);
        Assert.Single(concurrent, response => response.StatusCode is HttpStatusCode.BadRequest);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await CompleteResetAsync(client, link.Email, link.Token, NewPassword)).StatusCode);
    }

    [Theory]
    [InlineData("not-base64url")]
    [InlineData("AA")]
    public async Task MalformedTokenFailsSafely(string token)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();

        var response = await CompleteResetAsync(client, user.Email!, token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email!, factory.ValidPassword)).StatusCode);
    }

    [Fact]
    public async Task TamperedTokenFailsSafely()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());
        var last = link.Token[^1];
        var tampered = link.Token[..^1] + (last is 'A' ? 'B' : 'A');

        var response = await CompleteResetAsync(client, link.Email, tampered, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredTokenFailsWithoutLongSleep()
    {
        using var factory = new AuthWebApplicationFactory(passwordResetTokenLifetimeMilliseconds: 1);
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());
        await Task.Delay(20);

        var response = await CompleteResetAsync(client, link.Email, link.Token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PasswordPolicyFailureDoesNotChangePasswordOrConsumeToken()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());

        var weak = await CompleteResetAsync(client, link.Email, link.Token, "weakpassword");
        var valid = await CompleteResetAsync(client, link.Email, link.Token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task TokenRemainsValidAcrossRestartWithSameProtectedKeyRing()
    {
        using var storage = new TestAppStorage();
        ResetLink link;
        string email;
        using (var firstFactory = new AuthWebApplicationFactory(storage: storage))
        {
            await firstFactory.InitializeDatabaseAsync();
            var user = await firstFactory.CreateUserAsync(AccountApprovalStatus.Approved);
            email = user.Email!;
            using var firstClient = firstFactory.CreateHttpsClient();
            await RequestResetAsync(firstClient, email);
            link = ParseResetLink(await firstFactory.EmailSender.WaitForMessageAsync());
        }

        using var secondFactory = new AuthWebApplicationFactory(storage: storage);
        await secondFactory.InitializeDatabaseAsync();
        using var secondClient = secondFactory.CreateHttpsClient();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await CompleteResetAsync(secondClient, link.Email, link.Token, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(secondClient, email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task SensitiveValuesAreAbsentFromOperationalLogs()
    {
        using var factory = new AuthWebApplicationFactory(configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:FitnessApp"] = "Information",
                ["Logging:LogLevel:PasswordReset"] = "Information"
            });
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        await RequestResetAsync(client, user.Email!);
        var link = ParseResetLink(await factory.EmailSender.WaitForMessageAsync());
        await client.GetAsync($"/nulstil-adgangskode?email={Uri.EscapeDataString(link.Email)}&token={Uri.EscapeDataString(link.Token)}");
        await CompleteResetAsync(client, link.Email, link.Token, NewPassword);

        var logs = string.Join('\n', factory.LogSink.Entries);
        foreach (var eventId in new[] { 1300, 1302, 1310, 1320 })
        {
            Assert.Contains($"|{eventId}|", logs);
        }
        Assert.False(logs.Contains(NewPassword, StringComparison.Ordinal), "A submitted password was logged.");
        Assert.False(logs.Contains(factory.ValidPassword, StringComparison.Ordinal), "An existing password was logged.");
        Assert.False(logs.Contains(link.Token, StringComparison.Ordinal), "A raw reset token was logged.");
        Assert.False(
            logs.Contains("/nulstil-adgangskode?", StringComparison.Ordinal),
            "A complete reset URL was logged.");
        Assert.False(logs.Contains(user.Email!, StringComparison.OrdinalIgnoreCase), "An email address was logged.");
    }

    [Theory]
    [InlineData("PublicApp:BaseUrl", "http://untrusted.example.test")]
    [InlineData("Email:Transport", "Unknown")]
    [InlineData("Email:PickupDirectory", "wwwroot/pickup")]
    public async Task InvalidSecurityConfigurationFailsStartup(string key, string value)
    {
        using var factory = new AuthWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?> { [key] = value });

        await Assert.ThrowsAnyAsync<Exception>(factory.InitializeDatabaseAsync);
    }

    [Fact]
    public async Task SmtpModeWithoutPasswordFailsStartupWithoutFallback()
    {
        using var factory = new AuthWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Email:Transport"] = "Smtp",
                ["Smtp:Password"] = null
            });

        await Assert.ThrowsAnyAsync<Exception>(factory.InitializeDatabaseAsync);
    }

    private static Task<HttpResponseMessage> RequestResetAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(
            "api/auth/password-reset/request",
            new ForgotPasswordRequest { Email = email });

    private static Task<HttpResponseMessage> CompleteResetAsync(
        HttpClient client,
        string email,
        string token,
        string password) =>
        client.PostAsJsonAsync(
            "api/auth/password-reset/complete",
            new ResetPasswordRequest
            {
                Email = email,
                Token = token,
                NewPassword = password,
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

    private static Task<HttpResponseMessage> GetCurrentUserAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static ResetLink ParseResetLink(FitnessApp.Application.Email.EmailMessage message)
    {
        var url = message.PlainTextBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(line => line.StartsWith("https://", StringComparison.Ordinal));
        var uri = new Uri(url);
        var query = QueryHelpers.ParseQuery(uri.Query);
        return new ResetLink(uri, query["email"].Single()!, query["token"].Single()!);
    }

    private sealed record ResetLink(Uri Uri, string Email, string Token);
}
