using System.Globalization;
using FitnessApp.Application.Authentication;
using FitnessApp.Application.Calendar;
using FitnessApp.Domain.Settings;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Settings;

/// <summary>
/// Creates durable, owner-scoped in-app notifications from existing application state.
/// It intentionally has no delivery-provider dependency: notifications become visible only when the app is opened.
/// </summary>
internal sealed class InAppNotificationGenerator(
    FitnessDbContext db,
    ICalendarService calendarService,
    TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();

    public async Task EnsureTrainingRemindersAsync(
        string userId,
        UserSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.TrainingRemindersEnabled)
        {
            return;
        }

        var today = Today();
        var calendar = await calendarService.GetRangeAsync(userId, today, today, cancellationToken);
        var candidates = new List<NotificationCandidate>();

        foreach (var activity in calendar.Activities.Where(activity =>
                     activity.Date == today && activity.State is CalendarActivityState.Planned))
        {
            if (activity.Type is CalendarActivityType.Running && activity.RunningSessionId is { } sessionId)
            {
                candidates.Add(new NotificationCandidate(
                    $"training-running:{sessionId}",
                    InAppNotificationKind.TrainingReminder,
                    "Planlagt løbetur i dag",
                    "Der er en planlagt løbetur i kalenderen i dag.",
                    $"/loeb/plan/{sessionId}"));
            }
            else if (activity.Type is CalendarActivityType.Strength &&
                     activity.StrengthProgramId is { } programId &&
                     activity.StrengthWorkoutId is { } workoutId)
            {
                var originalDate = activity.PlannedDate ?? activity.Date;
                candidates.Add(new NotificationCandidate(
                    $"training-strength:{programId}:{workoutId}:{originalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                    InAppNotificationKind.TrainingReminder,
                    "Planlagt styrketræning i dag",
                    string.IsNullOrWhiteSpace(activity.Title)
                        ? "Der er planlagt styrketræning i kalenderen i dag."
                        : $"{activity.Title} er planlagt i dag.",
                    $"/styrke/{programId}/træninger/{workoutId}/aktiv?planlagt={originalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"));
            }
        }

        await PersistMissingAsync(userId, candidates, cancellationToken);
    }

    public async Task QueueNewPendingRegistrationNotificationsAsync(
        string registrationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
        {
            return;
        }

        var normalizedAdminRole = AuthenticationConstants.AdminRole.ToUpperInvariant();
        var administratorIds = await (
                from user in db.Users.AsNoTracking()
                join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where user.ApprovalStatus == AccountApprovalStatus.Approved &&
                      role.NormalizedName == normalizedAdminRole
                select user.Id)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (administratorIds.Length == 0)
        {
            return;
        }

        var optedOutAdministratorIds = await db.UserSettings.AsNoTracking()
            .Where(settings => administratorIds.Contains(settings.UserId) &&
                               !settings.AdminRequestNotificationsEnabled)
            .Select(settings => settings.UserId)
            .ToArrayAsync(cancellationToken);
        var optedOut = optedOutAdministratorIds.ToHashSet(StringComparer.Ordinal);
        var createdAtUtc = UtcNow();
        var candidates = administratorIds.Where(administratorId => !optedOut.Contains(administratorId))
            .Select(administratorId => new QueuedNotification(
                administratorId,
                $"registration-request:{registrationId}",
                InAppNotificationKind.AdminRegistrationRequest,
                "Ny brugeranmodning",
                "En ny brugeranmodning venter på behandling.",
                "/administration/registreringer",
                createdAtUtc))
            .ToArray();

        await QueueMissingAsync(candidates, cancellationToken);
    }

    private async Task PersistMissingAsync(
        string userId,
        IReadOnlyCollection<NotificationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var createdAtUtc = UtcNow();
        await QueueMissingAsync(candidates.Select(candidate => new QueuedNotification(
            userId,
            candidate.SourceKey,
            candidate.Kind,
            candidate.Title,
            candidate.Message,
            candidate.TargetPath,
            createdAtUtc)), cancellationToken);

        var addedNotifications = db.ChangeTracker.Entries<InAppNotification>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            // A second browser request can create the same source key between the read and insert.
            // The unique index is the final idempotency guard; leave unrelated tracked changes intact.
            foreach (var notification in addedNotifications)
            {
                db.Entry(notification).State = EntityState.Detached;
            }
        }
    }

    private async Task QueueMissingAsync(
        IEnumerable<QueuedNotification> notifications,
        CancellationToken cancellationToken)
    {
        var distinctNotifications = notifications
            .GroupBy(notification => new { notification.UserId, notification.SourceKey })
            .Select(group => group.First())
            .ToArray();
        if (distinctNotifications.Length == 0)
        {
            return;
        }

        var userIds = distinctNotifications.Select(notification => notification.UserId).Distinct().ToArray();
        var sourceKeys = distinctNotifications.Select(notification => notification.SourceKey).Distinct().ToArray();
        var existing = await db.InAppNotifications.AsNoTracking()
            .Where(notification => userIds.Contains(notification.UserId) && sourceKeys.Contains(notification.SourceKey))
            .Select(notification => new { notification.UserId, notification.SourceKey })
            .ToArrayAsync(cancellationToken);
        var existingKeys = existing
            .Select(notification => new NotificationKey(notification.UserId, notification.SourceKey))
            .ToHashSet();

        foreach (var notification in distinctNotifications)
        {
            if (existingKeys.Contains(new NotificationKey(notification.UserId, notification.SourceKey)))
            {
                continue;
            }

            db.InAppNotifications.Add(new InAppNotification
            {
                Id = Guid.NewGuid(),
                UserId = notification.UserId,
                SourceKey = notification.SourceKey,
                Kind = notification.Kind,
                Title = notification.Title,
                Message = notification.Message,
                TargetPath = notification.TargetPath,
                CreatedAtUtc = notification.CreatedAtUtc
            });
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone).DateTime);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };

    private static TimeZoneInfo FindCopenhagenTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }

    private sealed record NotificationCandidate(
        string SourceKey,
        InAppNotificationKind Kind,
        string Title,
        string Message,
        string TargetPath);

    private sealed record QueuedNotification(
        string UserId,
        string SourceKey,
        InAppNotificationKind Kind,
        string Title,
        string Message,
        string TargetPath,
        DateTime CreatedAtUtc);

    private readonly record struct NotificationKey(string UserId, string SourceKey);
}
