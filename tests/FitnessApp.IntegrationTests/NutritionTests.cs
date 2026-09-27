using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Nutrition;
using FitnessApp.Domain.Settings;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Nutrition;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class NutritionTests
{
    [Fact]
    public async Task FoodSearchCalculationsAndDuplicateSubmissionUseTheCatalogueSnapshot()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync(); await SeedCatalogue(factory);
        using var client = await Login(factory);
        var search = (await client.GetFromJsonAsync<FoodSearchPageResponse>("/api/nutrition/foods?query=aeble&page=1&pageSize=20"))!;
        var apple = Assert.Single(search.Items);
        Assert.Equal("Æble, rå", apple.Name);
        var id = Guid.NewGuid(); var date = new DateOnly(2026, 9, 26);
        var request = new AddNutritionFoodRequest { Id = id, Date = date, MealSlot = NutritionMealSlot.Breakfast, FoodId = apple.FoodId, Grams = 250 };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/nutrition/entries", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/nutrition/entries", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/nutrition/entries", new AddNutritionFoodRequest { Id = request.Id, Date = request.Date, MealSlot = request.MealSlot, FoodId = request.FoodId, Grams = 251 })).StatusCode);
        var day = (await client.GetFromJsonAsync<NutritionDayResponse>($"/api/nutrition/days/{date:yyyy-MM-dd}"))!;
        Assert.Equal(130m, day.Totals.EnergyKcal); Assert.Null(day.Totals.Sugar); Assert.Equal(250m, Assert.Single(day.Meals.Single(meal => meal.Slot == NutritionMealSlot.Breakfast).Entries).Grams);
        using var scope = factory.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().NutritionFoodEntries.CountAsync());
    }

    [Fact]
    public async Task DailyGoalsComeFromSettingsAndEmptyTotalsAreZero()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync(); await SeedCatalogue(factory);
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            db.UserSettings.Add(new UserSettings { UserId = user.Id, DailyCaloriesTarget = 2200.5m, ProteinTargetGrams = 130.5m });
            await db.SaveChangesAsync();
        }

        using var client = await Login(factory, user);
        var day = (await client.GetFromJsonAsync<NutritionDayResponse>("/api/nutrition/days/2026-09-26"))!;
        Assert.Equal(0m, day.Totals.EnergyKcal);
        Assert.Equal(2200.5m, day.Target?.EnergyKcal);
        Assert.Equal(130.5m, day.Target?.Protein);
    }

    [Fact]
    public async Task RecipeCreationAndAdditionAreIdempotentAndScaledAmountsAreValidated()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync(); await SeedCatalogue(factory);
        using var client = await Login(factory); var date = new DateOnly(2026, 9, 26);
        var createdEntry = await client.PostAsJsonAsync("/api/nutrition/entries", new AddNutritionFoodRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Breakfast, FoodId = 1, Grams = 10000 });
        Assert.Equal(HttpStatusCode.Created, createdEntry.StatusCode);
        var saveRequest = new CreateNutritionRecipeRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Breakfast, Name = "Stor portion", Portions = .1m };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/nutrition/recipes", saveRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/nutrition/recipes", saveRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/nutrition/recipes", new CreateNutritionRecipeRequest { Id = saveRequest.Id, Date = date, MealSlot = NutritionMealSlot.Breakfast, Name = "Ændret navn", Portions = .1m })).StatusCode);

        var recipes = (await client.GetFromJsonAsync<NutritionRecipeResponse[]>("/api/nutrition/recipes"))!;
        var recipe = Assert.Single(recipes);
        var addRequest = new AddNutritionRecipeRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Breakfast, Portions = .1m };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/nutrition/recipes/{recipe.Id}/entries", addRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/nutrition/recipes/{recipe.Id}/entries", addRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/nutrition/recipes/{recipe.Id}/entries", new AddNutritionRecipeRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Breakfast, Portions = 100 })).StatusCode);
        var day = (await client.GetFromJsonAsync<NutritionDayResponse>($"/api/nutrition/days/{date:yyyy-MM-dd}"))!;
        Assert.Equal(2, day.Meals.Single(meal => meal.Slot == NutritionMealSlot.Breakfast).Entries.Count);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().NutritionRecipeAdditions.CountAsync());
    }

    [Fact]
    public async Task SavedRecipesRemainOwnerScopedWhenAddedAsNestedResources()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync(); await SeedCatalogue(factory);
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var ownerClient = await Login(factory, owner); using var otherClient = await Login(factory);
        var date = new DateOnly(2026, 9, 26);
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync("/api/nutrition/entries", new AddNutritionFoodRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Lunch, FoodId = 1, Grams = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync("/api/nutrition/recipes", new CreateNutritionRecipeRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Lunch, Name = "Frokost", Portions = 1 })).StatusCode);
        var recipes = (await ownerClient.GetFromJsonAsync<NutritionRecipeResponse[]>("/api/nutrition/recipes"))!;
        var recipe = Assert.Single(recipes);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsJsonAsync($"/api/nutrition/recipes/{recipe.Id}/entries", new AddNutritionRecipeRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Dinner, Portions = 1 })).StatusCode);
        Assert.Empty((await otherClient.GetFromJsonAsync<NutritionRecipeResponse[]>("/api/nutrition/recipes"))!);
    }

    [Fact]
    public async Task FoodEntriesAreOwnerScoped()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync(); await SeedCatalogue(factory);
        using var owner = await Login(factory); using var other = await Login(factory); var date = new DateOnly(2026, 9, 26);
        var created = await owner.PostAsJsonAsync("/api/nutrition/entries", new AddNutritionFoodRequest { Id = Guid.NewGuid(), Date = date, MealSlot = NutritionMealSlot.Dinner, FoodId = 1, Grams = 100 });
        var day = (await created.Content.ReadFromJsonAsync<NutritionDayResponse>())!; var entry = Assert.Single(day.Meals.Single(meal => meal.Slot == NutritionMealSlot.Dinner).Entries);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/nutrition/entries/{entry.Id}", new UpdateNutritionFoodRequest { Grams = 300 })).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<NutritionDayResponse>($"/api/nutrition/days/{date:yyyy-MM-dd}"))!.Meals.SelectMany(meal => meal.Entries));
    }

    private static async Task SeedCatalogue(AuthWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        db.FridaCatalogueReleases.Add(new FridaCatalogueRelease { Id = FridaCatalogueRelease.Key, Version = "5.5", SourceUrl = "test", Checksum = "test", FoodCount = 1, ImportedAtUtc = DateTime.UtcNow });
        db.FridaFoods.Add(new FridaFood { FoodId = 1, DanishName = "Æble, rå", FoodGroup = "Frugt", SearchName = "aeble, raa", EnergyKcalPer100g = 52, ProteinPer100g = .3m, CarbohydratePer100g = 14, FatPer100g = .2m }); await db.SaveChangesAsync();
    }

    private static async Task<HttpClient> Login(AuthWebApplicationFactory factory)
    {
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        return await Login(factory, user);
    }

    private static async Task<HttpClient> Login(AuthWebApplicationFactory factory, ApplicationUser user)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword }); var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken); return client;
    }
}
