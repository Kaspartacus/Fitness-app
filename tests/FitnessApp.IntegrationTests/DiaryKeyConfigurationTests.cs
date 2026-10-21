using FitnessApp.Application.Diary;
using FitnessApp.Domain.Users;
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
        Assert.Equal(Path.GetFullPath(factory.Storage.KeyRingPath), repository.Directory.FullName);
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
