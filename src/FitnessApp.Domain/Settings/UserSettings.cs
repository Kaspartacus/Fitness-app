namespace FitnessApp.Domain.Settings;

public sealed class UserSettings
{
    public string UserId { get; set; } = string.Empty;

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? DailyCaloriesTarget { get; set; }

    public decimal? ProteinTargetGrams { get; set; }

    public decimal? CarbohydrateTargetGrams { get; set; }

    public decimal? FatTargetGrams { get; set; }

    public decimal? SugarTargetGrams { get; set; }

    public bool TrainingRemindersEnabled { get; set; } = true;

    public bool AdminRequestNotificationsEnabled { get; set; } = true;

    public bool IsGarminDemoConnected { get; set; }

    public DateTime? GarminDemoConnectedAtUtc { get; set; }
}
