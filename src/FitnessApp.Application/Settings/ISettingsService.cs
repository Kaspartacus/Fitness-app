namespace FitnessApp.Application.Settings;

public sealed record NutritionGoalsData(
    decimal? DailyCaloriesTarget,
    decimal? ProteinTargetGrams,
    decimal? CarbohydrateTargetGrams,
    decimal? FatTargetGrams,
    decimal? SugarTargetGrams);

public sealed record ProfileSettingsData(
    string DisplayName,
    decimal? HeightCm,
    decimal? WeightKg,
    NutritionGoalsData NutritionGoals);

public sealed record NotificationPreferencesData(
    bool RunningNotificationsEnabled,
    bool StrengthNotificationsEnabled,
    bool MealRemindersEnabled,
    bool NutritionProgressEnabled,
    bool DetailedPushPreviews,
    string TimeZoneId);

public sealed record GarminMockConnectionData(
    bool IsConnected,
    DateTimeOffset? ConnectedAt);

public sealed record SettingsOverviewData(
    ProfileSettingsData Profile,
    NotificationPreferencesData NotificationPreferences,
    GarminMockConnectionData Garmin,
    int UnreadNotificationCount,
    string Theme = "dark");

public sealed record UpdateProfileSettingsInput(
    string? DisplayName,
    decimal? HeightCm,
    decimal? WeightKg);

public sealed record UpdateNutritionGoalsInput(
    decimal? DailyCaloriesTarget,
    decimal? ProteinTargetGrams,
    decimal? CarbohydrateTargetGrams,
    decimal? FatTargetGrams,
    decimal? SugarTargetGrams);

public sealed record UpdateNotificationPreferencesInput(
    bool RunningNotificationsEnabled,
    bool StrengthNotificationsEnabled,
    bool MealRemindersEnabled,
    bool NutritionProgressEnabled,
    bool DetailedPushPreviews,
    string TimeZoneId);

public enum SettingsSaveStatus
{
    Saved,
    Invalid,
    NotFound
}

public sealed record ProfileSettingsResult(
    SettingsSaveStatus Status,
    ProfileSettingsData? Profile = null);

public sealed record NutritionGoalsResult(
    SettingsSaveStatus Status,
    NutritionGoalsData? Goals = null);

public sealed record NotificationPreferencesResult(
    SettingsSaveStatus Status,
    NotificationPreferencesData? Preferences = null);

public sealed record GarminMockConnectionResult(
    SettingsSaveStatus Status,
    GarminMockConnectionData? Connection = null);

public sealed record InAppNotificationData(
    Guid Id,
    string Kind,
    string Title,
    string Message,
    string TargetPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record ThemePreferenceResult(SettingsSaveStatus Status, string? Theme = null);

public interface ISettingsService
{
    Task<ThemePreferenceResult> UpdateThemeAsync(string userId, string? theme, CancellationToken cancellationToken);

    Task<SettingsOverviewData?> GetOverviewAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken);

    Task<ProfileSettingsResult> UpdateProfileAsync(
        string userId,
        UpdateProfileSettingsInput input,
        CancellationToken cancellationToken);

    Task<NutritionGoalsResult> UpdateNutritionGoalsAsync(
        string userId,
        UpdateNutritionGoalsInput input,
        CancellationToken cancellationToken);

    Task<NotificationPreferencesResult> UpdateNotificationPreferencesAsync(
        string userId,
        bool isAdministrator,
        UpdateNotificationPreferencesInput input,
        CancellationToken cancellationToken);

    Task<GarminMockConnectionResult> ConnectGarminDemoAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<GarminMockConnectionResult> DisconnectGarminDemoAsync(
        string userId,
        CancellationToken cancellationToken);

}
