namespace FitnessApp.Contracts.Nutrition;

public enum NutritionMealSlot
{
    Breakfast,
    MorningSnack,
    Lunch,
    AfternoonSnack,
    Dinner,
    EveningSnack
}

public sealed record FoodSearchResponse(
    int FoodId,
    string Name,
    string FoodGroup,
    decimal? EnergyKcalPer100g,
    decimal? ProteinPer100g,
    decimal? CarbohydratePer100g,
    decimal? FatPer100g,
    decimal? SugarPer100g);

public sealed record FoodSearchPageResponse(IReadOnlyList<FoodSearchResponse> Items, int Page, int PageSize, bool HasMore);

public sealed class AddNutritionFoodRequest
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public NutritionMealSlot MealSlot { get; set; }
    public int FoodId { get; set; }
    public decimal Grams { get; set; }
}

public sealed class UpdateNutritionFoodRequest
{
    public decimal Grams { get; set; }
}

public sealed record NutritionFoodEntryResponse(
    Guid Id,
    int? FoodId,
    string Name,
    string FoodGroup,
    decimal Grams,
    decimal? EnergyKcal,
    decimal? Protein,
    decimal? Carbohydrate,
    decimal? Fat,
    decimal? Sugar);

public sealed record NutritionMealResponse(NutritionMealSlot Slot, string Name, IReadOnlyList<NutritionFoodEntryResponse> Entries);

public sealed record NutritionTotalsResponse(decimal? EnergyKcal, decimal? Protein, decimal? Carbohydrate, decimal? Fat, decimal? Sugar);

public sealed record NutritionTargetResponse(decimal? EnergyKcal, decimal? Protein, decimal? Carbohydrate, decimal? Fat, decimal? Sugar);

public sealed record NutritionDayResponse(DateOnly Date, NutritionTotalsResponse Totals, NutritionTargetResponse? Target,
    IReadOnlyList<NutritionMealResponse> Meals, string CatalogueAttribution);

public sealed class UpdateNutritionTargetRequest
{
    public decimal? EnergyKcal { get; set; }
    public decimal? Protein { get; set; }
    public decimal? Carbohydrate { get; set; }
    public decimal? Fat { get; set; }
    public decimal? Sugar { get; set; }
}

public sealed class CreateNutritionRecipeRequest
{
    public DateOnly Date { get; set; }
    public NutritionMealSlot MealSlot { get; set; }
    public string? Name { get; set; }
    public decimal Portions { get; set; } = 1;
}

public sealed record NutritionRecipeResponse(Guid Id, string Name, decimal Portions, NutritionTotalsResponse Totals,
    int IngredientCount);

public sealed class AddNutritionRecipeRequest
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public NutritionMealSlot MealSlot { get; set; }
    public decimal Portions { get; set; } = 1;
}
