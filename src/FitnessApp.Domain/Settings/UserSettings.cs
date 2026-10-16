namespace FitnessApp.Domain.Settings;

public sealed class UserSettings
{
    public string Theme { get; set; } = "dark";

    public string UserId { get; set; } = string.Empty;

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? DailyCaloriesTarget { get; set; }

    public decimal? ProteinTargetGrams { get; set; }

    public decimal? CarbohydrateTargetGrams { get; set; }

    public decimal? FatTargetGrams { get; set; }

    public decimal? SugarTargetGrams { get; set; }

    public bool RunningNotificationsEnabled { get; set; } = true;
    public bool StrengthNotificationsEnabled { get; set; } = true;
    public bool MealRemindersEnabled { get; set; } = true;
    public bool NutritionProgressEnabled { get; set; } = true;
    public bool DetailedPushPreviews { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Copenhagen";

    public bool IsGarminDemoConnected { get; set; }

    public DateTime? GarminDemoConnectedAtUtc { get; set; }
}
