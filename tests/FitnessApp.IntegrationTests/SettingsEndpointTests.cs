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
        var nutrition = new UpdateNutritionGoalsRequest(2500.5m, 160.5m, 260.5m, 75.5m, 45.5m);

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
        Assert.Equal(2500.5m, ownerOverview.Profile.NutritionGoals.DailyCaloriesTarget);
        Assert.Equal(160.5m, ownerOverview.Profile.NutritionGoals.ProteinTargetGrams);
        Assert.Equal(260.5m, ownerOverview.Profile.NutritionGoals.CarbohydrateTargetGrams);
        Assert.Equal(75.5m, ownerOverview.Profile.NutritionGoals.FatTargetGrams);
        Assert.Equal(45.5m, ownerOverview.Profile.NutritionGoals.SugarTargetGrams);

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

        using var themeResponse = await client.PutAsJsonAsync($"{Settings}/theme", new UpdateThemePreferenceRequest("light"));
        Assert.Equal(HttpStatusCode.Unauthorized, themeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nutritionResponse.StatusCode);
    }

    [Fact]
    public async Task ThemeDefaultsToDarkAndPersistsOnlyForAuthenticatedOwner()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = await LoginAsync(factory, owner);
        using var otherClient = await LoginAsync(factory, other);
        Assert.Equal("dark", (await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.Theme);
        await AssertSavedAsync(client, $"{Settings}/theme", new UpdateThemePreferenceRequest("light"));
        await AssertSavedAsync(client, $"{Settings}/profile", new UpdateProfileSettingsRequest("Tema", 180, 75));
        using var reloaded = await LoginAsync(factory, owner);
        var overview = await reloaded.GetFromJsonAsync<SettingsOverviewResponse>(Settings);
        Assert.Equal("light", overview!.Theme);
        Assert.Equal(180, overview.Profile.HeightCm);
        Assert.Equal("dark", (await otherClient.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.Theme);
        await AssertSavedAsync(reloaded, $"{Settings}/theme", new UpdateThemePreferenceRequest("dark"));
        Assert.Equal(180, (await reloaded.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.Profile.HeightCm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("system")]
    [InlineData("Light")]
    public async Task InvalidThemeDoesNotOverwriteStoredPreference(string? theme)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = await LoginAsync(factory, owner);
        await AssertSavedAsync(client, $"{Settings}/theme", new UpdateThemePreferenceRequest("light"));
        using var response = await client.PutAsJsonAsync($"{Settings}/theme", new UpdateThemePreferenceRequest(theme));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("light", (await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.Theme);
    }

    [Fact]
    public async Task NotificationPreferencesPreserveDiaryRemindersWhenOmittedOrNullAndHonorExplicitFalse()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = await LoginAsync(factory, owner);
        var path = $"{Settings}/notification-preferences";

        await AssertSavedAsync(client, path,
            new UpdateNotificationPreferencesRequest(true, true, true, true, false, "Europe/Copenhagen", true));
        Assert.True((await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.NotificationPreferences.DiaryRemindersEnabled);

        // A browser running the previous contract sends no diary preference at all.
        await AssertSavedAsync(client, path, new
        {
            RunningNotificationsEnabled = false,
            StrengthNotificationsEnabled = true,
            MealRemindersEnabled = true,
            NutritionProgressEnabled = true,
            DetailedPushPreviews = false,
            TimeZoneId = "Europe/Copenhagen"
        });
        var afterLegacySave = (await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.NotificationPreferences;
        Assert.False(afterLegacySave.RunningNotificationsEnabled);
        Assert.True(afterLegacySave.DiaryRemindersEnabled);

        await AssertSavedAsync(client, path,
            new UpdateNotificationPreferencesRequest(false, true, true, true, false, "Europe/Copenhagen", null));
        Assert.True((await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.NotificationPreferences.DiaryRemindersEnabled);

        await AssertSavedAsync(client, path,
            new UpdateNotificationPreferencesRequest(false, true, true, true, false, "Europe/Copenhagen", false));
        Assert.False((await client.GetFromJsonAsync<SettingsOverviewResponse>(Settings))!.NotificationPreferences.DiaryRemindersEnabled);
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
