using System.Globalization;
using System.Text.Json;
using FitnessApp.Application.Calendar;
using FitnessApp.Application.Nutrition;
using FitnessApp.Domain.Nutrition;
using FitnessApp.Domain.Settings;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitnessApp.Infrastructure.Notifications;

public sealed class NotificationScheduler(FitnessDbContext db, ICalendarService calendar, INutritionService nutrition,
    TimeProvider clock, IWebPushSender sender, IOptions<WebPushOptions> options)
{
    public async Task TickAsync(CancellationToken ct)
    {
        var owners = await db.Users.AsNoTracking().Where(x => x.ApprovalStatus == AccountApprovalStatus.Approved).Select(x => x.Id).ToListAsync(ct);
        foreach (var owner in owners)
        {
            var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct) ?? new UserSettings { UserId = owner };
            foreach (var due in NotificationSchedule.Due(clock.GetUtcNow(), settings.TimeZoneId))
                await CreateAsync(owner, due, ct);
        }
        if (options.Value.Configured) await DeliverAsync(ct);
    }

    public async Task CreateAsync(string owner, ScheduledNotification due, CancellationToken ct)
    {
        // SQLite serializes the decision, cursor advancement, inbox row and device outbox in one transaction.
        // A primary key also guards the occurrence if two server workers overlap.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.NotificationOccurrences.AnyAsync(x => x.UserId == owner && x.Date == due.Date && x.Slot == due.Slot, ct)) return;
        if (!await db.Users.AnyAsync(x => x.Id == owner && x.ApprovalStatus == AccountApprovalStatus.Approved, ct)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < due.ScheduledAtUtc || now - due.ScheduledAtUtc > NotificationSchedule.LatenessWindow) return;
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct) ?? new UserSettings { UserId = owner };
        var previous = await db.InAppNotifications.Where(x => x.UserId == owner && x.LocalDate == due.Date && x.Kind == InAppNotificationKind.NutritionProgress)
            .Select(x => (long?)x.FoodSequence).MaxAsync(ct) ?? 0;
        var candidate = await CandidateAsync(owner, settings, due.Date, due.Slot, previous, ct);
        db.NotificationOccurrences.Add(new() { UserId = owner, Date = due.Date, Slot = due.Slot, ScheduledAtUtc = due.ScheduledAtUtc });
        if (candidate is not null)
        {
            var notification = new InAppNotification { Id = Guid.NewGuid(), UserId = owner,
                SourceKey = $"scheduled:{due.Date:yyyy-MM-dd}:{due.Slot}", Kind = candidate.Kind,
                Title = candidate.Title, Message = candidate.Message, TargetPath = candidate.Path,
                CreatedAtUtc = now, ScheduledAtUtc = due.ScheduledAtUtc, LocalDate = due.Date, ScheduleSlot = due.Slot,
                FoodSequence = candidate.Sequence, PreviousFoodSequence = previous };
            db.InAppNotifications.Add(notification);
            var devices = await ActiveDevices(owner).Select(x => x.Id).ToListAsync(ct);
            foreach (var device in devices) db.NotificationDeliveries.Add(new() { NotificationId = notification.Id, SubscriptionId = device, NextAttemptAtUtc = now });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    private IQueryable<WebPushSubscription> ActiveDevices(string owner) => db.WebPushSubscriptions.Where(x => x.UserId == owner &&
        db.UserSessions.Any(s => s.Id == x.SessionId && s.UserId == owner && s.RevokedAt == null &&
            db.Users.Any(u => u.Id == owner && u.ApprovalStatus == AccountApprovalStatus.Approved && u.SecurityStamp == s.SecurityStamp)));

    private async Task<Candidate?> CandidateAsync(string owner, UserSettings settings, DateOnly date, int slot, long previous, CancellationToken ct)
    {
        if (slot == 0)
        {
            var day = await calendar.GetRangeAsync(owner, date, date, ct);
            var activities = day.Activities.Where(x => x.Date == date && x.State == CalendarActivityState.Planned &&
                (x.Type == CalendarActivityType.Running ? settings.RunningNotificationsEnabled : settings.StrengthNotificationsEnabled)).ToArray();
            if (activities.Length == 0) return null;
            var path = NotificationService.CalendarPath(date);
            if (activities.Length == 1)
            {
                var activity = activities[0];
                if (activity.RunningSessionId is { } run) path = $"/loeb/plan/{run}";
                else if (activity.StrengthProgramId is { } program && activity.StrengthWorkoutId is { } workout)
                    path = $"/styrke/{program}/træninger/{workout}/aktiv?planlagt={activity.PlannedDate ?? date:yyyy-MM-dd}";
            }
            var names = string.Join(", ", activities.Select(x => string.IsNullOrWhiteSpace(x.Title)
                ? x.Type == CalendarActivityType.Running ? "Løbetur" : "Styrketræning" : x.Title));
            return new(InAppNotificationKind.TrainingReminder, "Dagens træning", Clip(names, 450), path, 0,
                activities.Length == 1 ? Clip(names, 120) : $"Du har {activities.Length} træninger i kalenderen i dag.");
        }
        var mealSlot = slot switch { 1 => NutritionMealSlot.Breakfast, 2 => NutritionMealSlot.Lunch, 3 => NutritionMealSlot.Dinner, _ => (NutritionMealSlot)(-1) };
        if ((int)mealSlot < 0) return null;
        var dayNutrition = await nutrition.GetDayAsync(owner, date, ct);
        var mealHasFood = dayNutrition.Meals.Any(x => x.Slot == mealSlot && x.Entries.Count > 0);
        var sequence = await db.NutritionLogAdditions.Where(x => x.UserId == owner && x.Date == date).Select(x => (long?)x.Sequence).MaxAsync(ct) ?? 0;
        var kind = NotificationSchedule.SelectNutrition(mealHasFood, settings.MealRemindersEnabled, settings.NutritionProgressEnabled, sequence, previous);
        var mealName = slot switch { 1 => "morgenmad", 2 => "frokost", _ => "aftensmad" };
        if (kind == InAppNotificationKind.MealReminder)
            return new(kind.Value, $"Husk din {mealName}", $"Du har ikke registreret {mealName} i dag.", $"/kost?dato={date:yyyy-MM-dd}&maaltid={mealSlot}", sequence, $"Husk at registrere din {mealName}.");
        if (kind != InAppNotificationKind.NutritionProgress) return null;
        var totals = dayNutrition.Totals; var target = dayNutrition.Target;
        var message = string.Join(" · ", new[] {
            Nutrient("Energi", totals.EnergyKcal, target?.EnergyKcal, "kcal"),
            Nutrient("Protein", totals.Protein, target?.Protein, "g"),
            Nutrient("Fedt", totals.Fat, target?.Fat, "g"),
            Nutrient("Kulhydrat", totals.Carbohydrate, target?.Carbohydrate, "g"),
            Nutrient("Sukker", totals.Sugar, target?.Sugar, "g") });
        return new(kind.Value, "Din kost i dag", message, $"/kost?dato={date:yyyy-MM-dd}", sequence,
            Nutrient("Energi", totals.EnergyKcal, target?.EnergyKcal, "kcal") + ". Se alle næringsstoffer i appen.");
    }

    public async Task DeliverAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.NotificationDeliveries.AsNoTracking().Where(x => !x.Finished && x.NextAttemptAtUtc <= now).OrderBy(x => x.NextAttemptAtUtc).Take(100).ToListAsync(ct);
        foreach (var delivery in pending)
        {
            now = clock.GetUtcNow().UtcDateTime;
            // Atomic lease; attempts are persisted before the network call, bounding retries even after crashes.
            var claimed = await db.NotificationDeliveries.Where(x => x.NotificationId == delivery.NotificationId && x.SubscriptionId == delivery.SubscriptionId && !x.Finished && x.NextAttemptAtUtc <= now && x.Attempts < 3)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, x => x.Attempts + 1).SetProperty(x => x.NextAttemptAtUtc, now.AddSeconds(60)), ct);
            if (claimed == 0)
            {
                if (delivery.Attempts >= 3) await FinishAsync(delivery, ct);
                continue;
            }
            db.ChangeTracker.Clear();
            var n = await db.InAppNotifications.SingleAsync(x => x.Id == delivery.NotificationId, ct);
            var device = await ActiveDevices(n.UserId).SingleOrDefaultAsync(x => x.Id == delivery.SubscriptionId, ct);
            if (device is null || n.ScheduledAtUtc is not { } scheduled || now - scheduled > NotificationSchedule.LatenessWindow)
            { await FinishAsync(delivery, ct); continue; }
            var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == n.UserId, ct) ?? new UserSettings();
            var shortBody = n.Message;
            if (n.Kind != InAppNotificationKind.PushTest)
            {
                if (n.LocalDate is not { } date || n.ScheduleSlot is not { } slot) { await FinishAsync(delivery, ct); continue; }
                var candidate = await CandidateAsync(n.UserId, settings, date, slot, n.PreviousFoodSequence, ct);
                if (candidate is null || candidate.Kind != n.Kind) { await FinishAsync(delivery, ct); continue; }
                n.Title = candidate.Title; n.Message = candidate.Message; n.TargetPath = candidate.Path;
                n.FoodSequence = candidate.Sequence;
                shortBody = candidate.ShortMessage;
                await db.SaveChangesAsync(ct);
            }
            var payload = JsonSerializer.Serialize(new { id = n.Id, binding = device.Binding,
                title = settings.DetailedPushPreviews ? n.Title : "FitnessApp",
                body = settings.DetailedPushPreviews ? shortBody : "Du har en ny besked i FitnessApp.",
                path = $"/notifikationer/{n.Id}" });
            var ttl = Math.Max(1, (int)(scheduled.Add(NotificationSchedule.LatenessWindow) - clock.GetUtcNow().UtcDateTime).TotalSeconds);
            var result = await sender.SendAsync(device, payload, n.Id.ToString("N"), ttl, ct);
            if (result == PushDeliveryResult.Expired)
                await db.WebPushSubscriptions.Where(x => x.Id == device.Id).ExecuteDeleteAsync(ct);
            else if (result != PushDeliveryResult.Retry || delivery.Attempts >= 2) await FinishAsync(delivery, ct);
            db.ChangeTracker.Clear();
        }
    }
    private Task FinishAsync(NotificationDelivery d, CancellationToken ct) => db.NotificationDeliveries.Where(x => x.NotificationId == d.NotificationId && x.SubscriptionId == d.SubscriptionId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Finished, true), ct);
    private static string Clip(string value, int length) => value.Length > length ? value[..length] + "…" : value;
    private static string Nutrient(string name, decimal? total, decimal? target, string unit) =>
        $"{name}: {(total is { } value ? value.ToString("0.#", CultureInfo.GetCultureInfo("da-DK")) : "ukendt")}" +
        (target is { } goal ? $" / {goal.ToString("0.#", CultureInfo.GetCultureInfo("da-DK"))}" : "") + $" {unit}";
    private sealed record Candidate(InAppNotificationKind Kind, string Title, string Message, string Path, long Sequence, string ShortMessage);
}
