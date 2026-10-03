using FitnessApp.Domain.Nutrition;

namespace FitnessApp.Infrastructure.Nutrition;

public sealed class FridaFood
{
    public int FoodId { get; set; }
    public string DanishName { get; set; } = string.Empty;
    public string FoodGroup { get; set; } = string.Empty;
    public string SearchName { get; set; } = string.Empty;
    // Complete Frida Data_Normalised values keyed by ParameterID. The displayed macro columns stay explicit.
    public string PublishedNutrientsJson { get; set; } = "{}";
    public decimal? EnergyKcalPer100g { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? CarbohydratePer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? SugarPer100g { get; set; }
}

public sealed class NutritionCustomFood
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SearchName { get; set; } = string.Empty;
    public decimal EnergyKcalPer100g { get; set; }
    public decimal ProteinPer100g { get; set; }
    public decimal CarbohydratePer100g { get; set; }
    public decimal FatPer100g { get; set; }
    public decimal SugarPer100g { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class FridaCatalogueRelease
{
    public const string Key = "frida";
    public string Id { get; set; } = Key;
    public string Version { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public int FoodCount { get; set; }
    public DateTime ImportedAtUtc { get; set; }
}

public sealed class NutritionMeal
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public NutritionMealSlot Slot { get; set; }
    public List<NutritionFoodEntry> Entries { get; set; } = [];
}

public sealed class NutritionFoodEntry
{
    public Guid Id { get; set; }
    public Guid MealId { get; set; }
    public NutritionMeal Meal { get; set; } = null!;
    public int? FoodId { get; set; }
    public Guid? CustomFoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FoodGroup { get; set; } = string.Empty;
    public string CatalogueVersion { get; set; } = string.Empty;
    public decimal Grams { get; set; }
    public decimal? EnergyKcalPer100g { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? CarbohydratePer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? SugarPer100g { get; set; }
}

public sealed class NutritionRecipe
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Portions { get; set; }
    public DateOnly CreatedFromDate { get; set; }
    public NutritionMealSlot CreatedFromSlot { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<NutritionRecipeIngredient> Ingredients { get; set; } = [];
}

public sealed class NutritionRecipeIngredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public NutritionRecipe Recipe { get; set; } = null!;
    public int? FoodId { get; set; }
    public Guid? CustomFoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FoodGroup { get; set; } = string.Empty;
    public string CatalogueVersion { get; set; } = string.Empty;
    public decimal Grams { get; set; }
    public decimal? EnergyKcalPer100g { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? CarbohydratePer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? SugarPer100g { get; set; }
}

public sealed class NutritionRecipeCreation
{
    public string UserId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public DateOnly Date { get; set; }
    public NutritionMealSlot Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Portions { get; set; }
}

public sealed class NutritionRecipeAddition
{
    public string UserId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public Guid RecipeId { get; set; }
    public DateOnly Date { get; set; }
    public NutritionMealSlot Slot { get; set; }
    public decimal Portions { get; set; }
}

public sealed class NutritionFoodAddition
{
    public string UserId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public DateOnly Date { get; set; }
    public NutritionMealSlot Slot { get; set; }
    public int? FoodId { get; set; }
    public Guid? CustomFoodId { get; set; }
    public decimal Grams { get; set; }
}
