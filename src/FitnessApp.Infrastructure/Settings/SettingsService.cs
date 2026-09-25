using FitnessApp.Application.Settings;
using FitnessApp.Domain.Settings;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Settings;

internal sealed class SettingsService(
    FitnessDbContext db,
    InAppNotificationGenerator notificationGenerator,
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
        await notificationGenerator.EnsureTrainingRemindersAsync(userId, settings, cancellationToken);
        var unreadNotificationCount = await db.InAppNotifications.AsNoTracking()
            .CountAsync(notification => notification.UserId == userId && notification.ReadAtUtc == null &&
                                        (isAdministrator || notification.Kind != InAppNotificationKind.AdminRegistrationRequest),
                cancellationToken);

        return new SettingsOverviewData(
            MapProfile(user.DisplayName, settings),
            MapPreferences(settings, isAdministrator),
            MapGarmin(settings),
            unreadNotificationCount);
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
        settings.TrainingRemindersEnabled = input.TrainingRemindersEnabled;
        if (isAdministrator)
        {
            settings.AdminRequestNotificationsEnabled = input.AdminRequestNotificationsEnabled;
        }
        await db.SaveChangesAsync(cancellationToken);

        return new NotificationPreferencesResult(
            SettingsSaveStatus.Saved,
            MapPreferences(settings, isAdministrator));
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

    public async Task<IReadOnlyCollection<InAppNotificationData>> GetNotificationsAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return [];
        }

        var settings = await GetOrCreateSettingsAsync(userId, cancellationToken);
        await notificationGenerator.EnsureTrainingRemindersAsync(userId, settings, cancellationToken);

        var notifications = await db.InAppNotifications.AsNoTracking()
            .Where(notification => notification.UserId == userId &&
                                   (isAdministrator || notification.Kind != InAppNotificationKind.AdminRegistrationRequest))
            .OrderBy(notification => notification.ReadAtUtc == null ? 0 : 1)
            .ThenByDescending(notification => notification.CreatedAtUtc)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        return notifications.Select(MapNotification).ToArray();
    }

    public async Task<NotificationReadStatus> MarkNotificationReadAsync(
        string userId,
        bool isAdministrator,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var readAtUtc = UtcNow();
        var affected = await db.InAppNotifications
            .Where(notification => notification.Id == notificationId && notification.UserId == userId &&
                                   notification.ReadAtUtc == null &&
                                   (isAdministrator || notification.Kind != InAppNotificationKind.AdminRegistrationRequest))
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAtUtc, readAtUtc),
                cancellationToken);
        if (affected == 1)
        {
            return NotificationReadStatus.Read;
        }

        var exists = await db.InAppNotifications.AsNoTracking()
            .AnyAsync(notification => notification.Id == notificationId && notification.UserId == userId &&
                                      (isAdministrator || notification.Kind != InAppNotificationKind.AdminRegistrationRequest),
                cancellationToken);
        return exists ? NotificationReadStatus.Read : NotificationReadStatus.NotFound;
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

    private static bool IsWithinRange(int? value, int minimum, int maximum) =>
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

    private static NotificationPreferencesData MapPreferences(UserSettings settings, bool isAdministrator) => new(
        settings.TrainingRemindersEnabled,
        isAdministrator && settings.AdminRequestNotificationsEnabled,
        isAdministrator);

    private static GarminMockConnectionData MapGarmin(UserSettings settings) => new(
        settings.IsGarminDemoConnected,
        settings.GarminDemoConnectedAtUtc is { } connectedAt
            ? new DateTimeOffset(DateTime.SpecifyKind(connectedAt, DateTimeKind.Utc))
            : null);

    private static InAppNotificationData MapNotification(InAppNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.Title,
        notification.Message,
        notification.TargetPath,
        new DateTimeOffset(DateTime.SpecifyKind(notification.CreatedAtUtc, DateTimeKind.Utc)),
        notification.ReadAtUtc is { } readAt
            ? new DateTimeOffset(DateTime.SpecifyKind(readAt, DateTimeKind.Utc))
            : null);

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
