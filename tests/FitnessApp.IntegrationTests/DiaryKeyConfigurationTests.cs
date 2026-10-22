using System.Text.Json;
using FitnessApp.Application.Diary;
using FitnessApp.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryKeyConfigurationTests
{
    [Fact]
    public async Task FinalHostConfigurationSelectsTheActualPersistentKeyRepository()
    {
        // The factory adds its isolated external path in ConfigureAppConfiguration, after
        // the minimal host's initial builder configuration. It must control actual persistence.
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var options = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        // The actual path can be canonicalized through system aliases such as macOS /var.
        // Verify directory identity through synthetic content rather than comparing alias spelling.
        const string marker = "synthetic-key-directory-probe";
        File.WriteAllText(Path.Combine(factory.Storage.KeyRingPath, marker), "synthetic");
        Assert.Equal("synthetic", File.ReadAllText(Path.Combine(repository.Directory.FullName, marker)));
    }

    [Fact]
    public async Task FinalHostConfigurationCannotPlaceKeysInsideTheApplication()
    {
        using var factory = new AuthWebApplicationFactory(configurationOverrides:
            new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = "App_Data/synthetic-rejected-diary-keys" });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(factory.InitializeDatabaseAsync);
        Assert.Contains("outside the repository/application", error.Message);
    }

    [Fact]
    public void ExternalAliasIntoApplicationCannotStoreKeysEvenWithMissingSuffix()
    {
        using var storage = new TestAppStorage();
        var root = Path.GetDirectoryName(storage.KeyRingPath)!;
        var application = CreateSyntheticApplication(storage);
        var alias = Path.Combine(root, "synthetic-external-alias"); Directory.CreateSymbolicLink(alias, application);
        var configuredKeys = Path.Combine(alias, "not-created", "keys");
        using var factory = new AuthWebApplicationFactory(storage: storage, configurationOverrides:
            new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = configuredKeys });
        using var host = factory.WithWebHostBuilder(builder => builder.UseContentRoot(application));
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("outside the repository/application", error.Message);
        Assert.False(Directory.Exists(Path.Combine(application, "not-created")));
    }

    [Fact]
    public void AliasedApplicationRootCannotHideItsActualKeyDirectory()
    {
        using var storage = new TestAppStorage();
        var root = Path.GetDirectoryName(storage.KeyRingPath)!;
        var application = CreateSyntheticApplication(storage);
        var alias = Path.Combine(root, "synthetic-content-alias"); Directory.CreateSymbolicLink(alias, application);
        using var factory = new AuthWebApplicationFactory(storage: storage, configurationOverrides:
            new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(application, "keys") });
        using var host = factory.WithWebHostBuilder(builder => builder.UseContentRoot(alias));
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("outside the repository/application", error.Message);
        Assert.False(Directory.Exists(Path.Combine(application, "keys")));
    }

    [Fact]
    public void ExternalDirectoryAliasUsesItsActualTargetAndCreatesOnlyTheMissingSuffix()
    {
        using var storage = new TestAppStorage();
        var root = Path.GetDirectoryName(storage.KeyRingPath)!;
        var application = CreateSyntheticApplication(storage);
        var external = Path.Combine(root, "synthetic-private-storage"); Directory.CreateDirectory(external);
        var alias = Path.Combine(root, "synthetic-private-alias"); Directory.CreateSymbolicLink(alias, external);
        using var factory = new AuthWebApplicationFactory(storage: storage, configurationOverrides:
            new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(alias, "nested", "keys") });
        using var host = factory.WithWebHostBuilder(builder => builder.UseContentRoot(application));
        using var client = host.CreateClient();
        var repository = Assert.IsType<FileSystemXmlRepository>(host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.DoesNotContain("synthetic-private-alias", repository.Directory.FullName);
        Assert.True(Directory.Exists(Path.Combine(external, "nested", "keys")));
        File.WriteAllText(Path.Combine(external, "nested", "keys", "synthetic-marker"), "synthetic");
        Assert.Equal("synthetic", File.ReadAllText(Path.Combine(repository.Directory.FullName, "synthetic-marker")));
    }

    private static string CreateSyntheticApplication(TestAppStorage storage)
    {
        var application = Path.Combine(Path.GetDirectoryName(storage.KeyRingPath)!, "synthetic-application");
        Directory.CreateDirectory(application);
        // Program reads the connection string before the factory applies its final configuration.
        File.WriteAllText(Path.Combine(application, "appsettings.json"), JsonSerializer.Serialize(new
        {
            ConnectionStrings = new { DefaultConnection = $"Data Source={storage.DatabasePath}" }
        }));
        return application;
    }

    [Fact]
    public async Task StartupAvailabilityCheckUsesTheSameConfiguredRingAndRejectsMissingKeys()
    {
        using var storage = new TestAppStorage();
        using (var first = new AuthWebApplicationFactory(storage: storage))
        {
            await first.InitializeDatabaseAsync();
            var owner = await first.CreateUserAsync(AccountApprovalStatus.Approved);
            await using var scope = first.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IDiaryService>();
            await service.ConfigureAsync(owner.Id, "synthetic-no-code-session", null, null, null, CancellationToken.None);
            var today = (await service.StateAsync(owner.Id, "synthetic-no-code-session", null, CancellationToken.None)).Value!.Today;
            Assert.Equal(DiaryStatus.Success, (await service.SaveAsync(owner.Id, "synthetic-no-code-session", null,
                today, "Synthetic key availability check", null, CancellationToken.None)).Status);
        }
        // Only isolated synthetic test keys are removed; no developer or deployed ring is used.
        Directory.Delete(storage.KeyRingPath, recursive: true);
        using var restarted = new AuthWebApplicationFactory(storage: storage);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(restarted.InitializeDatabaseAsync);
        Assert.Contains("Restore its backup", error.Message);
    }
}
