namespace FitnessApp.Contracts.Settings;

public sealed record NutritionGoalsResponse(
    decimal? DailyCaloriesTarget,
    decimal? ProteinTargetGrams,
    decimal? CarbohydrateTargetGrams,
    decimal? FatTargetGrams,
    decimal? SugarTargetGrams);

public sealed record ProfileSettingsResponse(
    string DisplayName,
    decimal? HeightCm,
    decimal? WeightKg,
    NutritionGoalsResponse NutritionGoals);

public sealed record UpdateProfileSettingsRequest(
    string? DisplayName,
    decimal? HeightCm,
    decimal? WeightKg);

public sealed record UpdateNutritionGoalsRequest(
    decimal? DailyCaloriesTarget,
    decimal? ProteinTargetGrams,
    decimal? CarbohydrateTargetGrams,
    decimal? FatTargetGrams,
    decimal? SugarTargetGrams);

public sealed record NotificationPreferencesResponse(
    bool RunningNotificationsEnabled,
    bool StrengthNotificationsEnabled,
    bool MealRemindersEnabled,
    bool NutritionProgressEnabled,
    bool DetailedPushPreviews,
    string TimeZoneId,
    bool DiaryRemindersEnabled = false);

public sealed record UpdateNotificationPreferencesRequest(
    bool RunningNotificationsEnabled,
    bool StrengthNotificationsEnabled,
    bool MealRemindersEnabled,
    bool NutritionProgressEnabled,
    bool DetailedPushPreviews,
    string TimeZoneId,
    bool DiaryRemindersEnabled = false);

public sealed record GarminMockConnectionResponse(
    bool IsConnected,
    DateTimeOffset? ConnectedAt);

public sealed record SettingsOverviewResponse(
    ProfileSettingsResponse Profile,
    NotificationPreferencesResponse NotificationPreferences,
    GarminMockConnectionResponse Garmin,
    int UnreadNotificationCount,
    string Theme = "dark");

public sealed record InAppNotificationResponse(
    Guid Id,
    string Kind,
    string Title,
    string Message,
    string TargetPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record ThemePreferenceResponse(string Theme);

public sealed record UpdateThemePreferenceRequest(string? Theme);
