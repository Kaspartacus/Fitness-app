namespace FitnessApp.Contracts.Settings;

public sealed record NutritionGoalsResponse(
    int? DailyCaloriesTarget,
    int? ProteinTargetGrams,
    int? CarbohydrateTargetGrams,
    int? FatTargetGrams,
    int? SugarTargetGrams);

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
    int? DailyCaloriesTarget,
    int? ProteinTargetGrams,
    int? CarbohydrateTargetGrams,
    int? FatTargetGrams,
    int? SugarTargetGrams);

public sealed record NotificationPreferencesResponse(
    bool TrainingRemindersEnabled,
    bool AdminRequestNotificationsEnabled,
    bool IsAdministrator);

public sealed record UpdateNotificationPreferencesRequest(
    bool TrainingRemindersEnabled,
    bool AdminRequestNotificationsEnabled);

public sealed record GarminMockConnectionResponse(
    bool IsConnected,
    DateTimeOffset? ConnectedAt);

public sealed record SettingsOverviewResponse(
    ProfileSettingsResponse Profile,
    NotificationPreferencesResponse NotificationPreferences,
    GarminMockConnectionResponse Garmin,
    int UnreadNotificationCount);

public sealed record InAppNotificationResponse(
    Guid Id,
    string Kind,
    string Title,
    string Message,
    string TargetPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record InAppNotificationListResponse(
    IReadOnlyCollection<InAppNotificationResponse> Notifications);
