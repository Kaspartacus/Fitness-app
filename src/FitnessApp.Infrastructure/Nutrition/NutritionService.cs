using FitnessApp.Application.Nutrition;
using FitnessApp.Domain.Nutrition;
using FitnessApp.Domain.Settings;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Nutrition;

public sealed class NutritionService(FitnessDbContext db, TimeProvider timeProvider) : INutritionService
{
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();
    private const string Attribution = "Data: DTU Fødevareinstituttet, Frida – Danmarks Fødevaredatabase, CC BY 4.0.";
    private static readonly NutritionMealSlot[] Slots = Enum.GetValues<NutritionMealSlot>();

    public async Task<NutritionDayData> GetDayAsync(string userId, DateOnly date, CancellationToken cancellationToken)
    {
        var meals = await db.NutritionMeals.AsNoTracking().Include(meal => meal.Entries)
            .Where(meal => meal.UserId == userId && meal.Date == date).ToListAsync(cancellationToken);
        var target = await GetTargetAsync(userId, cancellationToken);
        var responses = Slots.Select(slot => new NutritionMealData(slot, SlotName(slot), meals.Where(meal => meal.Slot == slot)
            .SelectMany(meal => meal.Entries).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Select(Map).ToArray())).ToArray();
        return new NutritionDayData(date, Sum(responses.SelectMany(meal => meal.Entries)), target, responses, Attribution);
    }

    public async Task<FoodSearchPageData> SearchFoodsAsync(string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Clamp(page, 1, 10_000); pageSize = Math.Clamp(pageSize, 1, 30);
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return new FoodSearchPageData([], page, pageSize, false);
        IQueryable<FridaFood> foods = db.FridaFoods.AsNoTracking();
        foreach (var term in terms) foods = foods.Where(food => EF.Functions.Like(food.SearchName, $"%{EscapeLikeTerm(term)}%", "\\"));
        var normalized = string.Join(' ', terms);
        var candidates = await foods.OrderBy(food => food.SearchName == normalized ? 0 : food.SearchName.StartsWith(normalized) ? 1 : 2)
            .ThenBy(food => food.DanishName).ThenBy(food => food.FoodId).Skip((page - 1) * pageSize).Take(pageSize + 1).ToListAsync(cancellationToken);
        return new FoodSearchPageData(candidates.Take(pageSize).Select(Map).ToArray(), page, pageSize, candidates.Count > pageSize);
    }

    public async Task<NutritionOperationResult> AddFoodAsync(string userId, AddNutritionFoodInput request, CancellationToken cancellationToken)
    {
        if (request is null || request.Id == Guid.Empty || !IsValidDate(request.Date) || !IsValidSlot(request.MealSlot) || !ValidGrams(request.Grams))
            return new(NutritionOperationStatus.Invalid);
        var existing = await db.NutritionFoodEntries.Include(entry => entry.Meal)
            .SingleOrDefaultAsync(entry => entry.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Meal.UserId != userId) return new(NutritionOperationStatus.NotFound);
            return existing.Meal.Date == request.Date && existing.Meal.Slot == request.MealSlot &&
                   existing.FoodId == request.FoodId && existing.Grams == request.Grams
                ? new(NutritionOperationStatus.Saved, await GetDayAsync(userId, existing.Meal.Date, cancellationToken))
                : new(NutritionOperationStatus.Conflict);
        }
        var food = await db.FridaFoods.AsNoTracking().SingleOrDefaultAsync(item => item.FoodId == request.FoodId, cancellationToken);
        if (food is null) return new(NutritionOperationStatus.NotFound);
        var release = await db.FridaCatalogueReleases.AsNoTracking().SingleOrDefaultAsync(item => item.Id == FridaCatalogueRelease.Key, cancellationToken);
        if (release is null) return new(NutritionOperationStatus.Conflict);
        var meal = await db.NutritionMeals.SingleOrDefaultAsync(item => item.UserId == userId && item.Date == request.Date && item.Slot == request.MealSlot, cancellationToken);
        if (meal is null) { meal = new() { Id = Guid.NewGuid(), UserId = userId, Date = request.Date, Slot = request.MealSlot }; db.NutritionMeals.Add(meal); }
        meal.Entries.Add(Snapshot(request.Id, food, request.Grams, release.Version));
        try { await db.SaveChangesAsync(cancellationToken); return new(NutritionOperationStatus.Saved, await GetDayAsync(userId, request.Date, cancellationToken)); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(NutritionOperationStatus.Conflict); }
    }

    public async Task<NutritionOperationResult> UpdateFoodAsync(string userId, Guid entryId, UpdateNutritionFoodInput request, CancellationToken cancellationToken)
    {
        if (entryId == Guid.Empty || request is null || !ValidGrams(request.Grams)) return new(NutritionOperationStatus.Invalid);
        var entry = await db.NutritionFoodEntries.Include(item => item.Meal).SingleOrDefaultAsync(item => item.Id == entryId && item.Meal.UserId == userId, cancellationToken);
        if (entry is null) return new(NutritionOperationStatus.NotFound);
        entry.Grams = request.Grams; await db.SaveChangesAsync(cancellationToken);
        return new(NutritionOperationStatus.Saved, await GetDayAsync(userId, entry.Meal.Date, cancellationToken));
    }

    public async Task<NutritionOperationResult> DeleteFoodAsync(string userId, Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await db.NutritionFoodEntries.Include(item => item.Meal).SingleOrDefaultAsync(item => item.Id == entryId && item.Meal.UserId == userId, cancellationToken);
        if (entry is null) return new(NutritionOperationStatus.NotFound);
        var date = entry.Meal.Date; db.NutritionFoodEntries.Remove(entry); await db.SaveChangesAsync(cancellationToken);
        return new(NutritionOperationStatus.Saved, await GetDayAsync(userId, date, cancellationToken));
    }

    public async Task<NutritionTargetData?> GetTargetAsync(string userId, CancellationToken cancellationToken)
    {
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        return settings is null
            ? null
            : new NutritionTargetData(settings.DailyCaloriesTarget, settings.ProteinTargetGrams,
                settings.CarbohydrateTargetGrams, settings.FatTargetGrams, settings.SugarTargetGrams);
    }

    public async Task<IReadOnlyList<NutritionRecipeData>> ListRecipesAsync(string userId, CancellationToken cancellationToken)
    {
        var recipes = await db.NutritionRecipes.AsNoTracking().Include(recipe => recipe.Ingredients).Where(recipe => recipe.UserId == userId)
            .OrderBy(recipe => recipe.Name).ToListAsync(cancellationToken);
        return recipes.Select(recipe => new NutritionRecipeData(recipe.Id, recipe.Name, recipe.Portions, Sum(recipe.Ingredients.Select(Map)), recipe.Ingredients.Count)).ToArray();
    }

    public async Task<NutritionOperationStatus> CreateRecipeAsync(string userId, CreateNutritionRecipeInput request, CancellationToken cancellationToken)
    {
        if (request is null || request.Id == Guid.Empty || !IsValidDate(request.Date) || !IsValidSlot(request.MealSlot) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 || request.Portions is < .1m or > 100) return NutritionOperationStatus.Invalid;
        var existingRecipe = await db.NutritionRecipes.AsNoTracking().SingleOrDefaultAsync(recipe => recipe.Id == request.Id, cancellationToken);
        if (existingRecipe is not null)
            return Matches(existingRecipe, userId, request) ? NutritionOperationStatus.Saved : NutritionOperationStatus.Conflict;
        var entries = await db.NutritionMeals.AsNoTracking().Where(item => item.UserId == userId && item.Date == request.Date && item.Slot == request.MealSlot)
            .SelectMany(item => item.Entries).ToListAsync(cancellationToken);
        if (entries.Count == 0) return NutritionOperationStatus.NotFound;
        var recipe = new NutritionRecipe
        {
            Id = request.Id,
            UserId = userId,
            Name = request.Name.Trim(),
            Portions = request.Portions,
            CreatedFromDate = request.Date,
            CreatedFromSlot = request.MealSlot,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        recipe.Ingredients.AddRange(entries.Select(Snapshot));
        db.NutritionRecipes.Add(recipe);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return NutritionOperationStatus.Saved;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var racedRecipe = await db.NutritionRecipes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
            return racedRecipe is not null && Matches(racedRecipe, userId, request)
                ? NutritionOperationStatus.Saved
                : NutritionOperationStatus.Conflict;
        }
    }

    public async Task<NutritionOperationResult> AddRecipeAsync(string userId, Guid recipeId, AddNutritionRecipeInput request, CancellationToken cancellationToken)
    {
        if (recipeId == Guid.Empty || request is null || request.Id == Guid.Empty || !IsValidDate(request.Date) || !IsValidSlot(request.MealSlot) || request.Portions is < .1m or > 100) return new(NutritionOperationStatus.Invalid);
        var previousAddition = await db.NutritionRecipeAdditions.AsNoTracking()
            .SingleOrDefaultAsync(addition => addition.UserId == userId && addition.RequestId == request.Id, cancellationToken);
        if (previousAddition is not null)
            return Matches(previousAddition, recipeId, request)
                ? new(NutritionOperationStatus.Saved, await GetDayAsync(userId, previousAddition.Date, cancellationToken))
                : new(NutritionOperationStatus.Conflict);
        var recipe = await db.NutritionRecipes.AsNoTracking().Include(item => item.Ingredients).SingleOrDefaultAsync(item => item.Id == recipeId && item.UserId == userId, cancellationToken);
        if (recipe is null) return new(NutritionOperationStatus.NotFound);
        var factor = request.Portions / recipe.Portions;
        var scaledIngredients = recipe.Ingredients.Select(ingredient => (Ingredient: ingredient, Grams: ingredient.Grams * factor)).ToArray();
        if (scaledIngredients.Any(item => !ValidGrams(item.Grams))) return new(NutritionOperationStatus.Invalid);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var meal = await db.NutritionMeals.SingleOrDefaultAsync(item => item.UserId == userId && item.Date == request.Date && item.Slot == request.MealSlot, cancellationToken) ?? new NutritionMeal { Id = Guid.NewGuid(), UserId = userId, Date = request.Date, Slot = request.MealSlot };
        if (meal.Entries.Count == 0 && db.Entry(meal).State == EntityState.Detached) db.NutritionMeals.Add(meal);
        foreach (var item in scaledIngredients) meal.Entries.Add(Snapshot(Guid.NewGuid(), item.Ingredient, item.Grams));
        db.NutritionRecipeAdditions.Add(new NutritionRecipeAddition
        {
            UserId = userId,
            RequestId = request.Id,
            RecipeId = recipeId,
            Date = request.Date,
            Slot = request.MealSlot,
            Portions = request.Portions
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(NutritionOperationStatus.Saved, await GetDayAsync(userId, request.Date, cancellationToken));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var racedAddition = await db.NutritionRecipeAdditions.AsNoTracking()
                .SingleOrDefaultAsync(addition => addition.UserId == userId && addition.RequestId == request.Id, cancellationToken);
            return racedAddition is not null && Matches(racedAddition, recipeId, request)
                ? new(NutritionOperationStatus.Saved, await GetDayAsync(userId, racedAddition.Date, cancellationToken))
                : new(NutritionOperationStatus.Conflict);
        }
    }

    private static NutritionFoodEntry Snapshot(Guid id, FridaFood food, decimal grams, string version) => new() { Id = id, FoodId = food.FoodId, Name = food.DanishName, FoodGroup = food.FoodGroup, CatalogueVersion = version, Grams = grams, EnergyKcalPer100g = food.EnergyKcalPer100g, ProteinPer100g = food.ProteinPer100g, CarbohydratePer100g = food.CarbohydratePer100g, FatPer100g = food.FatPer100g, SugarPer100g = food.SugarPer100g };
    private static NutritionRecipeIngredient Snapshot(NutritionFoodEntry entry) => new() { Id = Guid.NewGuid(), FoodId = entry.FoodId, Name = entry.Name, FoodGroup = entry.FoodGroup, CatalogueVersion = entry.CatalogueVersion, Grams = entry.Grams, EnergyKcalPer100g = entry.EnergyKcalPer100g, ProteinPer100g = entry.ProteinPer100g, CarbohydratePer100g = entry.CarbohydratePer100g, FatPer100g = entry.FatPer100g, SugarPer100g = entry.SugarPer100g };
    private static NutritionFoodEntry Snapshot(Guid id, NutritionRecipeIngredient item, decimal grams) => new() { Id = id, FoodId = item.FoodId, Name = item.Name, FoodGroup = item.FoodGroup, CatalogueVersion = item.CatalogueVersion, Grams = grams, EnergyKcalPer100g = item.EnergyKcalPer100g, ProteinPer100g = item.ProteinPer100g, CarbohydratePer100g = item.CarbohydratePer100g, FatPer100g = item.FatPer100g, SugarPer100g = item.SugarPer100g };
    private static FoodSearchData Map(FridaFood item) => new(item.FoodId, item.DanishName, item.FoodGroup, item.EnergyKcalPer100g, item.ProteinPer100g, item.CarbohydratePer100g, item.FatPer100g, item.SugarPer100g);
    private static NutritionFoodEntryData Map(NutritionFoodEntry item) => new(item.Id, item.FoodId, item.Name, item.FoodGroup, item.Grams, Scale(item.EnergyKcalPer100g, item.Grams), Scale(item.ProteinPer100g, item.Grams), Scale(item.CarbohydratePer100g, item.Grams), Scale(item.FatPer100g, item.Grams), Scale(item.SugarPer100g, item.Grams));
    private static NutritionFoodEntryData Map(NutritionRecipeIngredient item) => new(item.Id, item.FoodId, item.Name, item.FoodGroup, item.Grams, Scale(item.EnergyKcalPer100g, item.Grams), Scale(item.ProteinPer100g, item.Grams), Scale(item.CarbohydratePer100g, item.Grams), Scale(item.FatPer100g, item.Grams), Scale(item.SugarPer100g, item.Grams));
    private static NutritionTotalsData Sum(IEnumerable<NutritionFoodEntryData> entries) => new(SumValue(entries.Select(item => item.EnergyKcal)), SumValue(entries.Select(item => item.Protein)), SumValue(entries.Select(item => item.Carbohydrate)), SumValue(entries.Select(item => item.Fat)), SumValue(entries.Select(item => item.Sugar)));
    private static decimal? SumValue(IEnumerable<decimal?> values)
    {
        var data = values.ToArray();
        if (data.Length == 0) return 0m;
        return data.Any(value => value is null) ? null : data.Sum(value => value!.Value);
    }
    private static decimal? Scale(decimal? value, decimal grams) => value is null ? null : value * grams / 100m;
    private static bool ValidGrams(decimal grams) => grams is > 0 and <= 10000;
    private static bool IsValidSlot(NutritionMealSlot slot) => Enum.IsDefined(slot);
    private bool IsValidDate(DateOnly date)
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        return date >= new DateOnly(2000, 1, 1) && date <= today.AddDays(366);
    }
    private static string SlotName(NutritionMealSlot slot) => slot switch { NutritionMealSlot.Breakfast => "Morgenmad", NutritionMealSlot.MorningSnack => "Mellemmåltid · Formiddag", NutritionMealSlot.Lunch => "Frokost", NutritionMealSlot.AfternoonSnack => "Mellemmåltid · Eftermiddag", NutritionMealSlot.Dinner => "Aftensmad", NutritionMealSlot.EveningSnack => "Mellemmåltid · Aften", _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa");
    private static string EscapeLikeTerm(string term) => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    private static bool Matches(NutritionRecipeAddition addition, Guid recipeId, AddNutritionRecipeInput request) =>
        addition.RecipeId == recipeId && addition.Date == request.Date && addition.Slot == request.MealSlot && addition.Portions == request.Portions;
    private static bool Matches(NutritionRecipe recipe, string userId, CreateNutritionRecipeInput request) =>
        recipe.UserId == userId && recipe.Name == request.Name!.Trim() && recipe.Portions == request.Portions &&
        recipe.CreatedFromDate == request.Date && recipe.CreatedFromSlot == request.MealSlot;
    private static TimeZoneInfo FindCopenhagenTimeZone() { try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); } }
}
