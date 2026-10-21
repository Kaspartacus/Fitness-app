namespace FitnessApp.Domain.Settings;

public sealed record ScheduledNotification(DateOnly Date, int Slot, DateTime ScheduledAtUtc);

public static class NotificationSchedule
{
    // A short recovery window prevents obsolete reminders being sent after an outage.
    public static readonly TimeSpan LatenessWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeOnly[] Times = [new(7, 0), new(10, 0), new(14, 0), new(19, 30), new(20, 0)];

    public static bool IsValidTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public static IReadOnlyList<ScheduledNotification> Due(DateTimeOffset now, string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var due = new List<ScheduledNotification>();
        for (var slot = 0; slot < Times.Length; slot++)
        {
            var local = today.ToDateTime(Times[slot], DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) continue;
            var utc = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime
                : TimeZoneInfo.ConvertTimeToUtc(local, zone);
            if (now.UtcDateTime >= utc && now.UtcDateTime - utc <= LatenessWindow)
                due.Add(new(today, slot, utc));
        }
        return due;
    }

    public static InAppNotificationKind? SelectNutrition(bool mealHasFood, bool reminders, bool progress,
        long newFoodSequence, long previousSequence) => !mealHasFood
        ? reminders ? InAppNotificationKind.MealReminder : null
        : progress && newFoodSequence > previousSequence ? InAppNotificationKind.NutritionProgress : null;
}
