using FitnessApp.Domain.Nutrition;

namespace FitnessApp.Application.Nutrition;

public enum NutritionOperationStatus { Saved, NotFound, Invalid, Conflict }

public sealed record FoodSearchData(int? FoodId, Guid? CustomFoodId, string Name, string FoodGroup, decimal? EnergyKcalPer100g, decimal? ProteinPer100g, decimal? CarbohydratePer100g, decimal? FatPer100g, decimal? SugarPer100g);
public sealed record FoodSearchPageData(IReadOnlyList<FoodSearchData> Items, int Page, int PageSize, bool HasMore);
public sealed record CreateCustomNutritionFoodInput(Guid Id, string? Name, decimal EnergyKcalPer100g, decimal ProteinPer100g, decimal CarbohydratePer100g, decimal FatPer100g, decimal SugarPer100g);
public sealed record AddNutritionFoodInput(Guid Id, DateOnly Date, NutritionMealSlot MealSlot, int? FoodId, Guid? CustomFoodId, decimal Grams);
public sealed record UpdateNutritionFoodInput(decimal ExpectedGrams, decimal Grams);
public sealed record NutritionFoodEntryData(Guid Id, int? FoodId, string Name, string FoodGroup, decimal Grams, decimal? EnergyKcal, decimal? Protein, decimal? Carbohydrate, decimal? Fat, decimal? Sugar);
public sealed record NutritionMealData(NutritionMealSlot Slot, string Name, IReadOnlyList<NutritionFoodEntryData> Entries);
public sealed record NutritionTotalsData(decimal? EnergyKcal, decimal? Protein, decimal? Carbohydrate, decimal? Fat, decimal? Sugar);
public sealed record NutritionTargetData(decimal? EnergyKcal, decimal? Protein, decimal? Carbohydrate, decimal? Fat, decimal? Sugar);
public sealed record NutritionDayData(DateOnly Date, NutritionTotalsData Totals, NutritionTargetData? Target, IReadOnlyList<NutritionMealData> Meals, string CatalogueAttribution);
public sealed record CreateNutritionRecipeInput(Guid Id, DateOnly Date, NutritionMealSlot MealSlot, string? Name, decimal Portions);
public sealed record NutritionRecipeData(Guid Id, string Name, decimal Portions, NutritionTotalsData Totals, int IngredientCount);
public sealed record AddNutritionRecipeInput(Guid Id, DateOnly Date, NutritionMealSlot MealSlot, decimal Portions);
public sealed record NutritionOperationResult(NutritionOperationStatus Status, NutritionDayData? Day = null);

public interface INutritionService
{
    Task<NutritionDayData> GetDayAsync(string userId, DateOnly date, CancellationToken cancellationToken);
    Task<FoodSearchPageData> SearchFoodsAsync(string userId, string query, int page, int pageSize, CancellationToken cancellationToken);
    Task<(NutritionOperationStatus Status, FoodSearchData? Food)> CreateCustomFoodAsync(string userId, CreateCustomNutritionFoodInput request, CancellationToken cancellationToken);
    Task<NutritionOperationResult> AddFoodAsync(string userId, AddNutritionFoodInput request, CancellationToken cancellationToken);
    Task<NutritionOperationResult> UpdateFoodAsync(string userId, Guid entryId, UpdateNutritionFoodInput request, CancellationToken cancellationToken);
    Task<NutritionOperationResult> DeleteFoodAsync(string userId, Guid entryId, CancellationToken cancellationToken);
    Task<NutritionTargetData?> GetTargetAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NutritionRecipeData>> ListRecipesAsync(string userId, CancellationToken cancellationToken);
    Task<NutritionOperationStatus> CreateRecipeAsync(string userId, CreateNutritionRecipeInput request, CancellationToken cancellationToken);
    Task<NutritionOperationStatus> DeleteRecipeAsync(string userId, Guid recipeId, CancellationToken cancellationToken);
    Task<NutritionOperationResult> AddRecipeAsync(string userId, Guid recipeId, AddNutritionRecipeInput request, CancellationToken cancellationToken);
}
