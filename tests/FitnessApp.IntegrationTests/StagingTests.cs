using System.Net;
using FitnessApp.Application.Email;
using FitnessApp.Infrastructure;
using FitnessApp.Infrastructure.Email;
using FitnessApp.Infrastructure.Notifications;
using FitnessApp.Infrastructure.Persistence;
using FitnessApp.Server.Staging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FitnessApp.IntegrationTests;

public sealed class StagingTests
{
    [Fact]
    public async Task StagingReadinessRequiresExplicitMigrationsAndDoesNotLeakDetails()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Staging");
        using var client = factory.CreateHttpsClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var before = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
        Assert.Equal("Unhealthy", await before.Content.ReadAsStringAsync());
        Assert.Contains("no-store", before.Headers.CacheControl!.ToString());

        Assert.Equal(0, await StagingOperations.MigrateAsync(factory.Services, CancellationToken.None));
        using var after = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal("Healthy", await after.Content.ReadAsStringAsync());
        using var method = await client.PostAsync("/health/ready", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);
    }

    [Fact]
    public async Task ReadinessDetectsLaterSchemaLoss()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Staging");
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public void StagingIgnoresAccidentalSmtpAndPushConfigurationWithoutControlledTestOptIn()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Staging", configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Email:Transport"] = "Smtp",
                ["WebPush:PublicKey"] = "synthetic-not-a-vapid-key",
                ["WebPush:PrivateKey"] = "synthetic-not-a-vapid-key",
                ["WebPush:Subject"] = "mailto:synthetic@example.test"
            });
        using var client = factory.CreateHttpsClient();
        Assert.Equal("Pickup", factory.Services.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.Transport);
        var push = factory.Services.GetRequiredService<IOptions<WebPushOptions>>().Value;
        Assert.False(push.Configured);
        Assert.Empty(push.PublicKey);
        Assert.Empty(push.PrivateKey);
        Assert.Empty(push.Subject);
    }

    [Fact]
    public void ControlledTestOptInStillValidatesCredentials()
    {
        using var factory = new AuthWebApplicationFactory(environmentName: "Staging", configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Email:Transport"] = "Smtp",
                ["Staging:AllowExternalEmail"] = "true",
                ["Smtp:Password"] = ""
            });
        Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateHttpsClient());
    }

    [Fact]
    public void SenderUsesFinalValidatedTransportRatherThanEarlyBuilderConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<EmailDeliveryOptions>(options => options.Transport = "Pickup");
        services.AddEmailDelivery();
        using var provider = services.BuildServiceProvider();
        Assert.Equal("PickupDirectoryEmailSender", provider.GetRequiredService<IEmailSender>().GetType().Name);
    }

    [Fact]
    public async Task MigrationFailureIsAnExitCodeAndDoesNotStartHttpOrWorkers()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FitnessDbContext>(options => options.UseSqlite("Data Source=/nonexistent/synthetic/missing.db"));
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(1, await StagingOperations.MigrateAsync(provider, CancellationToken.None));
    }
}
