using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Settings;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;

namespace FitnessApp.IntegrationTests;

public sealed class SettingsEndpointTests
{
    private const string Settings = "api/settings";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SectionSpecificSavesPreserveOtherSettingsForStaleTabsAndOtherOwners(bool nutritionSavesFirst)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var otherUser = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var profileTab = await LoginAsync(factory, owner);
        using var nutritionTab = await LoginAsync(factory, owner);
        using var otherClient = await LoginAsync(factory, otherUser);

        var staleProfileOverview = await profileTab.GetFromJsonAsync<SettingsOverviewResponse>(Settings);
        var staleNutritionOverview = await nutritionTab.GetFromJsonAsync<SettingsOverviewResponse>(Settings);
        Assert.NotNull(staleProfileOverview);
        Assert.NotNull(staleNutritionOverview);
        Assert.Null(staleProfileOverview.Profile.NutritionGoals.DailyCaloriesTarget);
        Assert.Null(staleNutritionOverview.Profile.HeightCm);

        var profile = new UpdateProfileSettingsRequest("Opdateret profil", 181.5m, 80.5m);
        var nutrition = new UpdateNutritionGoalsRequest(2500, 160, 260, 75, 45);

        if (nutritionSavesFirst)
        {
            await AssertSavedAsync(nutritionTab, $"{Settings}/nutrition-goals", nutrition);
            await AssertSavedAsync(profileTab, $"{Settings}/profile", profile);
        }
        else
        {
            await AssertSavedAsync(profileTab, $"{Settings}/profile", profile);
            await AssertSavedAsync(nutritionTab, $"{Settings}/nutrition-goals", nutrition);
        }

        var ownerOverview = await profileTab.GetFromJsonAsync<SettingsOverviewResponse>(Settings);
        Assert.NotNull(ownerOverview);
        Assert.Equal("Opdateret profil", ownerOverview.Profile.DisplayName);
        Assert.Equal(181.5m, ownerOverview.Profile.HeightCm);
        Assert.Equal(80.5m, ownerOverview.Profile.WeightKg);
        Assert.Equal(2500, ownerOverview.Profile.NutritionGoals.DailyCaloriesTarget);
        Assert.Equal(160, ownerOverview.Profile.NutritionGoals.ProteinTargetGrams);
        Assert.Equal(260, ownerOverview.Profile.NutritionGoals.CarbohydrateTargetGrams);
        Assert.Equal(75, ownerOverview.Profile.NutritionGoals.FatTargetGrams);
        Assert.Equal(45, ownerOverview.Profile.NutritionGoals.SugarTargetGrams);

        var otherOverview = await otherClient.GetFromJsonAsync<SettingsOverviewResponse>(Settings);
        Assert.NotNull(otherOverview);
        Assert.Equal("Testbruger", otherOverview.Profile.DisplayName);
        Assert.Null(otherOverview.Profile.HeightCm);
        Assert.Null(otherOverview.Profile.WeightKg);
        Assert.Null(otherOverview.Profile.NutritionGoals.DailyCaloriesTarget);
        Assert.Null(otherOverview.Profile.NutritionGoals.ProteinTargetGrams);
        Assert.Null(otherOverview.Profile.NutritionGoals.CarbohydrateTargetGrams);
        Assert.Null(otherOverview.Profile.NutritionGoals.FatTargetGrams);
        Assert.Null(otherOverview.Profile.NutritionGoals.SugarTargetGrams);
    }

    [Fact]
    public async Task SettingsSectionUpdatesRequireAnAuthenticatedOwner()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        using var profileResponse = await client.PutAsJsonAsync(
            $"{Settings}/profile",
            new UpdateProfileSettingsRequest("Uautoriseret", 180m, 75m));
        using var nutritionResponse = await client.PutAsJsonAsync(
            $"{Settings}/nutrition-goals",
            new UpdateNutritionGoalsRequest(2200, 150, 220, 70, 40));

        Assert.Equal(HttpStatusCode.Unauthorized, profileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nutritionResponse.StatusCode);
    }

    private static async Task AssertSavedAsync<T>(HttpClient client, string path, T request)
    {
        using var response = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpClient> LoginAsync(AuthWebApplicationFactory factory, ApplicationUser user)
    {
        var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest { Email = user.Email!, Password = factory.ValidPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}
