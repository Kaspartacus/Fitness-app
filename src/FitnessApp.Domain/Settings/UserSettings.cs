namespace FitnessApp.Domain.Settings;

public sealed class UserSettings
{
    public string UserId { get; set; } = string.Empty;

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public int? DailyCaloriesTarget { get; set; }

    public int? ProteinTargetGrams { get; set; }

    public int? CarbohydrateTargetGrams { get; set; }

    public int? FatTargetGrams { get; set; }

    public int? SugarTargetGrams { get; set; }

    public bool TrainingRemindersEnabled { get; set; } = true;

    public bool AdminRequestNotificationsEnabled { get; set; } = true;

    public bool IsGarminDemoConnected { get; set; }

    public DateTime? GarminDemoConnectedAtUtc { get; set; }
}
