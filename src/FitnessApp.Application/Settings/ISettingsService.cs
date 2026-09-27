namespace FitnessApp.Application.Settings;

public sealed record NutritionGoalsData(
    int? DailyCaloriesTarget,
    int? ProteinTargetGrams,
    int? CarbohydrateTargetGrams,
    int? FatTargetGrams,
    int? SugarTargetGrams);

public sealed record ProfileSettingsData(
    string DisplayName,
    decimal? HeightCm,
    decimal? WeightKg,
    NutritionGoalsData NutritionGoals);

public sealed record NotificationPreferencesData(
    bool TrainingRemindersEnabled,
    bool AdminRequestNotificationsEnabled,
    bool IsAdministrator);

public sealed record GarminMockConnectionData(
    bool IsConnected,
    DateTimeOffset? ConnectedAt);

public sealed record SettingsOverviewData(
    ProfileSettingsData Profile,
    NotificationPreferencesData NotificationPreferences,
    GarminMockConnectionData Garmin,
    int UnreadNotificationCount);

public sealed record UpdateProfileSettingsInput(
    string? DisplayName,
    decimal? HeightCm,
    decimal? WeightKg);

public sealed record UpdateNutritionGoalsInput(
    int? DailyCaloriesTarget,
    int? ProteinTargetGrams,
    int? CarbohydrateTargetGrams,
    int? FatTargetGrams,
    int? SugarTargetGrams);

public sealed record UpdateNotificationPreferencesInput(
    bool TrainingRemindersEnabled,
    bool AdminRequestNotificationsEnabled);

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

public enum NotificationReadStatus
{
    Read,
    NotFound
}

public interface ISettingsService
{
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

    Task<IReadOnlyCollection<InAppNotificationData>> GetNotificationsAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken);

    Task<NotificationReadStatus> MarkNotificationReadAsync(
        string userId,
        bool isAdministrator,
        Guid notificationId,
        CancellationToken cancellationToken);
}
