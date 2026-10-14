using FitnessApp.Application.Notifications;
using FitnessApp.Application.Settings;
using FitnessApp.Domain.Settings;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitnessApp.Infrastructure.Notifications;

public sealed class NotificationService(FitnessDbContext db, TimeProvider clock, IOptions<WebPushOptions> options) : INotificationService
{
    public async Task<NotificationPageData> GetInboxAsync(string owner, bool isAdministrator, int page, CancellationToken ct)
    {
        var rows = await Visible(owner, isAdministrator).AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(page * 25).Take(26).ToListAsync(ct);
        return new(rows.Take(25).Select(Map).ToArray(), rows.Count > 25, await UnreadAsync(owner, isAdministrator, ct));
    }
    public Task<int> UnreadAsync(string owner, bool isAdministrator, CancellationToken ct) => Visible(owner, isAdministrator).CountAsync(x => x.ReadAtUtc == null, ct);
    public async Task MarkReadAsync(string owner, bool isAdministrator, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await Visible(owner, isAdministrator).Where(x => ids.Contains(x.Id) && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, now), ct);
    }
    public async Task<NotificationDestinationData> DestinationAsync(string owner, bool isAdministrator, Guid id, CancellationToken ct)
    {
        var notification = await Visible(owner, isAdministrator).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (notification is null) return new("/indstillinger/notifikationer", true);
        var path = notification.TargetPath;
        // Destinations are generated internally, but still enforce a narrow same-origin path allowlist.
        if (!IsSafePath(path)) return new("/indstillinger/notifikationer", true);
        if (path.StartsWith("/loeb/plan/", StringComparison.Ordinal))
        {
            var segment = path.Split('?', 2)[0].Split('/').Last();
            if (!Guid.TryParse(segment, out var session) || !await db.RunningSessions.AnyAsync(x => x.Id == session && db.RunningPlans.Any(p => p.Id == x.PlanId && p.UserId == owner), ct))
                return new(CalendarPath(notification.LocalDate), true);
        }
        if (path.StartsWith("/styrke/", StringComparison.Ordinal))
        {
            var parts = path.Split('?', 2)[0].Split('/');
            if (parts.Length < 5 || !Guid.TryParse(parts[2], out var program) || !Guid.TryParse(parts[4], out var workout) ||
                !await db.ProgramWorkouts.AnyAsync(x => x.Id == workout && x.ProgramId == program && db.StrengthPrograms.Any(p => p.Id == program && p.UserId == owner), ct))
                return new(CalendarPath(notification.LocalDate), true);
        }
        return new(path, false);
    }
    // isAdministrator comes only from the server principal, whose roles are refreshed at validation.
    private IQueryable<InAppNotification> Visible(string owner, bool isAdministrator) =>
        db.InAppNotifications.Where(x => x.UserId == owner &&
            (isAdministrator || x.Kind != InAppNotificationKind.AdminRegistrationRequest));

    public static bool IsSafePath(string path) => path.Length <= 300 && !path.Contains('\\') && !path.Contains('%') && !path.Any(char.IsControl) &&
        (path.StartsWith("/loeb/plan/", StringComparison.Ordinal) || path.StartsWith("/styrke/", StringComparison.Ordinal) ||
         path.StartsWith("/kalender?", StringComparison.Ordinal) || path.StartsWith("/kost?", StringComparison.Ordinal) ||
         path is "/indstillinger/notifikationer" or "/administration/registreringer");
    internal static string CalendarPath(DateOnly? date) => date is { } day ? $"/kalender?maaned={day:yyyy-MM}&dato={day:yyyy-MM-dd}" : "/kalender";

    public async Task<PushDeviceData> DeviceAsync(string owner, string session, string? binding, string? installationId, CancellationToken ct) =>
        new(options.Value.Configured, options.Value.Configured ? options.Value.PublicKey : null,
            binding is not null && await db.WebPushSubscriptions.AnyAsync(x => x.UserId == owner && x.SessionId == session && x.Binding == binding, ct),
            installationId is not null && await db.WebPushSubscriptions.AnyAsync(x => x.UserId == owner && x.InstallationId == installationId, ct));

    public async Task<PushSaveStatus> SubscribeAsync(string owner, string session, SubscribePushInput input, CancellationToken ct)
    {
        if (!options.Value.Configured) return PushSaveStatus.Unavailable;
        if (!PushEndpointPolicy.IsAllowed(input.Endpoint) || !PushEndpointPolicy.ValidKey(input.P256dh, 65) ||
            !PushEndpointPolicy.ValidKey(input.Auth, 16) || !Guid.TryParseExact(input.Binding, "N", out _) ||
            !Guid.TryParseExact(input.InstallationId, "N", out _)) return PushSaveStatus.Invalid;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Reclaim only this owner's permanently inactive devices. Expired access tokens/sessions
        // deliberately remain eligible: phones must continue receiving push while the app is closed.
        await db.WebPushSubscriptions.Where(x => x.UserId == owner && !db.UserSessions.Any(s =>
            s.Id == x.SessionId && s.UserId == owner && s.RevokedAt == null &&
            db.Users.Any(u => u.Id == owner && u.SecurityStamp == s.SecurityStamp))).ExecuteDeleteAsync(ct);
        var existing = await db.WebPushSubscriptions.SingleOrDefaultAsync(x => x.Endpoint == input.Endpoint || x.Binding == input.Binding, ct);
        // Neither the rotating binding nor the installation ID authorizes cross-owner modification.
        if (existing is not null && (existing.UserId != owner || existing.Binding != input.Binding ||
            existing.Endpoint != input.Endpoint || existing.InstallationId != input.InstallationId)) return PushSaveStatus.Conflict;
        // An installation survives browser logout/login; replace its prior subscription for this
        // authenticated owner rather than consuming another quota slot after each login.
        await db.WebPushSubscriptions.Where(x => x.UserId == owner && x.InstallationId == input.InstallationId &&
            x.Endpoint != input.Endpoint).ExecuteDeleteAsync(ct);
        if (existing is null)
        {
            if (await db.WebPushSubscriptions.CountAsync(x => x.UserId == owner, ct) >= 10) return PushSaveStatus.Limited;
            existing = new WebPushSubscription { Id = Guid.NewGuid(), UserId = owner, Endpoint = input.Endpoint, Binding = input.Binding, InstallationId = input.InstallationId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
            db.WebPushSubscriptions.Add(existing);
        }
        existing.SessionId = session; existing.P256dh = input.P256dh; existing.Auth = input.Auth;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return PushSaveStatus.Saved;
    }
    public Task UnsubscribeAsync(string owner, string installationId, CancellationToken ct) =>
        // Installation identity survives privacy-first local clearing. It never authorizes another owner's rows.
        db.WebPushSubscriptions.Where(x => x.UserId == owner && x.InstallationId == installationId).ExecuteDeleteAsync(ct);

    public async Task<PushSaveStatus> TestAsync(string owner, string binding, CancellationToken ct)
    {
        if (!options.Value.Configured) return PushSaveStatus.Unavailable;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var device = await db.WebPushSubscriptions.SingleOrDefaultAsync(x => x.UserId == owner && x.Binding == binding, ct);
        if (device is null) return PushSaveStatus.NotFound;
        // Persisted account-wide cooldown survives restarts and cannot be bypassed by re-subscribing.
        if (await db.InAppNotifications.AnyAsync(x => x.UserId == owner && x.Kind == InAppNotificationKind.PushTest && x.CreatedAtUtc > now.AddMinutes(-5), ct)) return PushSaveStatus.Limited;
        var notification = new InAppNotification { Id = Guid.NewGuid(), UserId = owner, Kind = InAppNotificationKind.PushTest,
            SourceKey = "test:" + Guid.NewGuid().ToString("N"), Title = "Test fra FitnessApp", Message = "Telefonnotifikationer er klar på denne enhed.",
            TargetPath = "/indstillinger/notifikationer", CreatedAtUtc = now, ScheduledAtUtc = now };
        db.InAppNotifications.Add(notification);
        db.NotificationDeliveries.Add(new() { NotificationId = notification.Id, SubscriptionId = device.Id, NextAttemptAtUtc = now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return PushSaveStatus.Saved;
    }
    private static InAppNotificationData Map(InAppNotification n) => new(n.Id, n.Kind.ToString(), n.Title, n.Message, n.TargetPath,
        new(DateTime.SpecifyKind(n.CreatedAtUtc, DateTimeKind.Utc)), n.ReadAtUtc is { } read ? new DateTimeOffset(DateTime.SpecifyKind(read, DateTimeKind.Utc)) : null);
}
