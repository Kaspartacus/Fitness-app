using FitnessApp.Application.Settings;
using FitnessApp.Domain.Settings;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Settings;

internal sealed class SettingsService(
    FitnessDbContext db,
    TimeProvider timeProvider) : ISettingsService
{
    public async Task<SettingsOverviewData?> GetOverviewAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        var unreadNotificationCount = await db.InAppNotifications.AsNoTracking()
            .CountAsync(notification => notification.UserId == userId && notification.ReadAtUtc == null &&
                (isAdministrator || notification.Kind != InAppNotificationKind.AdminRegistrationRequest),
                cancellationToken);

        return new SettingsOverviewData(
            MapProfile(user.DisplayName, settings),
            MapPreferences(settings),
            MapGarmin(settings),
            unreadNotificationCount,
            settings.Theme);
    }

    public async Task<ThemePreferenceResult> UpdateThemeAsync(
        string userId, string? theme, CancellationToken cancellationToken)
    {
        if (theme is not ("light" or "dark")) return new(SettingsSaveStatus.Invalid);
        if (!await UserExistsAsync(userId, cancellationToken)) return new(SettingsSaveStatus.NotFound);
        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        settings.Theme = theme;
        await db.SaveChangesAsync(cancellationToken);
        return new(SettingsSaveStatus.Saved, settings.Theme);
    }

    public async Task<ProfileSettingsResult> UpdateProfileAsync(
        string userId,
        UpdateProfileSettingsInput input,
        CancellationToken cancellationToken)
    {
        if (input is null || !IsValidProfile(input))
        {
            return new ProfileSettingsResult(SettingsSaveStatus.Invalid);
        }

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new ProfileSettingsResult(SettingsSaveStatus.NotFound);
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        user.DisplayName = input.DisplayName!.Trim();
        settings.HeightCm = input.HeightCm;
        settings.WeightKg = input.WeightKg;
        await db.SaveChangesAsync(cancellationToken);

        return new ProfileSettingsResult(SettingsSaveStatus.Saved, MapProfile(user.DisplayName, settings));
    }

    public async Task<NutritionGoalsResult> UpdateNutritionGoalsAsync(
        string userId,
        UpdateNutritionGoalsInput input,
        CancellationToken cancellationToken)
    {
        if (input is null || !IsValidNutritionGoals(input))
        {
            return new NutritionGoalsResult(SettingsSaveStatus.Invalid);
        }

        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new NutritionGoalsResult(SettingsSaveStatus.NotFound);
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        settings.DailyCaloriesTarget = input.DailyCaloriesTarget;
        settings.ProteinTargetGrams = input.ProteinTargetGrams;
        settings.CarbohydrateTargetGrams = input.CarbohydrateTargetGrams;
        settings.FatTargetGrams = input.FatTargetGrams;
        settings.SugarTargetGrams = input.SugarTargetGrams;
        await db.SaveChangesAsync(cancellationToken);

        return new NutritionGoalsResult(SettingsSaveStatus.Saved, MapNutritionGoals(settings));
    }

    public async Task<NotificationPreferencesResult> UpdateNotificationPreferencesAsync(
        string userId,
        bool isAdministrator,
        UpdateNotificationPreferencesInput input,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return new NotificationPreferencesResult(SettingsSaveStatus.Invalid);
        }

        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new NotificationPreferencesResult(SettingsSaveStatus.NotFound);
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        if (!FitnessApp.Domain.Settings.NotificationSchedule.IsValidTimeZone(input.TimeZoneId))
            return new NotificationPreferencesResult(SettingsSaveStatus.Invalid);
        settings.RunningNotificationsEnabled = input.RunningNotificationsEnabled;
        settings.StrengthNotificationsEnabled = input.StrengthNotificationsEnabled;
        settings.MealRemindersEnabled = input.MealRemindersEnabled;
        settings.NutritionProgressEnabled = input.NutritionProgressEnabled;
        settings.DetailedPushPreviews = input.DetailedPushPreviews;
        settings.TimeZoneId = input.TimeZoneId;
        await db.SaveChangesAsync(cancellationToken);

        return new NotificationPreferencesResult(
            SettingsSaveStatus.Saved,
            MapPreferences(settings));
    }

    public async Task<GarminMockConnectionResult> ConnectGarminDemoAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new GarminMockConnectionResult(SettingsSaveStatus.NotFound);
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        if (!settings.IsGarminDemoConnected)
        {
            settings.IsGarminDemoConnected = true;
            settings.GarminDemoConnectedAtUtc = UtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return new GarminMockConnectionResult(SettingsSaveStatus.Saved, MapGarmin(settings));
    }

    public async Task<GarminMockConnectionResult> DisconnectGarminDemoAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new GarminMockConnectionResult(SettingsSaveStatus.NotFound);
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        if (settings.IsGarminDemoConnected || settings.GarminDemoConnectedAtUtc is not null)
        {
            settings.IsGarminDemoConnected = false;
            settings.GarminDemoConnectedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new GarminMockConnectionResult(SettingsSaveStatus.Saved, MapGarmin(settings));
    }

    private async Task<UserSettings> GetOrCreateSettingsAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await db.UserSettings.SingleOrDefaultAsync(settings => settings.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var settings = new UserSettings { UserId = userId };
        db.UserSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return settings;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            db.Entry(settings).State = EntityState.Detached;
            return await db.UserSettings.SingleAsync(candidate => candidate.UserId == userId, cancellationToken);
        }
    }

    private Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(candidate => candidate.Id == userId, cancellationToken);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsValidProfile(UpdateProfileSettingsInput input) =>
        input.DisplayName?.Trim().Length is >= 1 and <= 100 &&
        IsWithinRange(input.HeightCm, 50m, 300m) &&
        IsWithinRange(input.WeightKg, 20m, 500m);

    private static bool IsValidNutritionGoals(UpdateNutritionGoalsInput input) =>
        IsWithinRange(input.DailyCaloriesTarget, 500, 10_000) &&
        IsWithinRange(input.ProteinTargetGrams, 0, 1_000) &&
        IsWithinRange(input.CarbohydrateTargetGrams, 0, 1_000) &&
        IsWithinRange(input.FatTargetGrams, 0, 1_000) &&
        IsWithinRange(input.SugarTargetGrams, 0, 1_000);

    private static bool IsWithinRange(decimal? value, decimal minimum, decimal maximum) =>
        value is null || value >= minimum && value <= maximum;

    private static ProfileSettingsData MapProfile(string displayName, UserSettings settings) => new(
        displayName,
        settings.HeightCm,
        settings.WeightKg,
        MapNutritionGoals(settings));

    private static NutritionGoalsData MapNutritionGoals(UserSettings settings) => new(
        settings.DailyCaloriesTarget,
        settings.ProteinTargetGrams,
        settings.CarbohydrateTargetGrams,
        settings.FatTargetGrams,
        settings.SugarTargetGrams);

    private static NotificationPreferencesData MapPreferences(UserSettings settings) => new(
        settings.RunningNotificationsEnabled, settings.StrengthNotificationsEnabled,
        settings.MealRemindersEnabled, settings.NutritionProgressEnabled,
        settings.DetailedPushPreviews, settings.TimeZoneId);

    private static GarminMockConnectionData MapGarmin(UserSettings settings) => new(
        settings.IsGarminDemoConnected,
        settings.GarminDemoConnectedAtUtc is { } connectedAt
            ? new DateTimeOffset(DateTime.SpecifyKind(connectedAt, DateTimeKind.Utc))
            : null);

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
