using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Notifications;
using FitnessApp.Application.Authentication;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Notifications;
using FitnessApp.Contracts.Settings;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Nutrition;
using FitnessApp.Domain.Running;
using FitnessApp.Domain.Settings;
using FitnessApp.Domain.Strength;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Calendar;
using FitnessApp.Infrastructure.Notifications;
using FitnessApp.Infrastructure.Nutrition;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FitnessApp.IntegrationTests;

public sealed class NotificationTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);
    private static readonly IOptions<WebPushOptions> Configured = Options.Create(new WebPushOptions { PublicKey = "synthetic", PrivateKey = "synthetic", Subject = "mailto:operator@example.test" });

    [Theory]
    [InlineData("2026-03-28T06:00:00Z", 0)]
    [InlineData("2026-03-29T05:00:00Z", 0)]
    [InlineData("2026-10-24T05:00:00Z", 0)]
    [InlineData("2026-10-25T06:00:00Z", 0)]
    [InlineData("2026-10-25T09:00:00Z", 1)]
    [InlineData("2026-10-25T13:00:00Z", 2)]
    [InlineData("2026-10-25T18:30:00Z", 3)]
    public void ScheduleFollowsCopenhagenDstAndLocalSlots(string utc, int slot)
    {
        var now = DateTimeOffset.Parse(utc);
        var due = Assert.Single(NotificationSchedule.Due(now, "Europe/Copenhagen"));
        Assert.Equal(slot, due.Slot);
        Assert.Equal(now.UtcDateTime, due.ScheduledAtUtc);
        Assert.Empty(NotificationSchedule.Due(now.AddMinutes(16), "Europe/Copenhagen"));
        Assert.Empty(NotificationSchedule.Due(now.AddSeconds(-1), "Europe/Copenhagen"));
    }

    [Fact]
    public void ScheduleUsesConfiguredZoneAndNeverAddsDiarySlot()
    {
        Assert.Equal(0, Assert.Single(NotificationSchedule.Due(DateTimeOffset.Parse("2026-07-01T11:00:00Z"), "America/New_York")).Slot);
        Assert.Empty(NotificationSchedule.Due(DateTimeOffset.Parse("2026-07-01T18:00:00Z"), "Europe/Copenhagen"));
        Assert.False(NotificationSchedule.IsValidTimeZone("not/a-zone"));
    }

    [Theory]
    [InlineData(false, true, true, 2, 1, InAppNotificationKind.MealReminder)]
    [InlineData(false, false, true, 2, 1, null)]
    [InlineData(true, true, true, 1, 0, InAppNotificationKind.NutritionProgress)]
    [InlineData(true, true, true, 2, 2, null)]
    [InlineData(true, true, false, 2, 1, null)]
    public void MealAndSummarySelectionIsExclusive(bool filled, bool reminders, bool progress, long sequence, long previous, InAppNotificationKind? expected) =>
        Assert.Equal(expected, NotificationSchedule.SelectNutrition(filled, reminders, progress, sequence, previous));

    [Fact]
    public async Task SavedFoodJournalSurvivesEditDeleteAndRestartWithoutSpuriousSummaries()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        db.UserSettings.Add(new() { UserId = owner.Id, MealRemindersEnabled = false, DailyCaloriesTarget = 2000, ProteinTargetGrams = 100 });
        var breakfast = Meal(owner.Id, NutritionMealSlot.Breakfast); var lunch = Meal(owner.Id, NutritionMealSlot.Lunch); var dinner = Meal(owner.Id, NutritionMealSlot.Dinner);
        db.NutritionMeals.AddRange(breakfast, lunch, dinner); await db.SaveChangesAsync();
        var initialSequence = await db.NutritionLogAdditions.MaxAsync(x => x.Sequence);
        await CreateAt(db, owner.Id, 1);
        var first = Assert.Single(await db.InAppNotifications.ToArrayAsync());
        Assert.Equal(InAppNotificationKind.NutritionProgress, first.Kind);
        Assert.Contains("Energi: 300 / 2000 kcal", first.Message);
        Assert.Contains("Sukker: ukendt g", first.Message);
        Assert.DoesNotContain("Sukker: 0", first.Message);
        db.ChangeTracker.Clear();
        await db.NutritionFoodEntries.Where(x => x.MealId == breakfast.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Grams, 150));
        await db.NutritionFoodEntries.Where(x => x.MealId == lunch.Id).ExecuteDeleteAsync();
        Assert.Equal(initialSequence, await db.NutritionLogAdditions.MaxAsync(x => x.Sequence));
        await CreateAt(db, owner.Id, 2); // empty lunch with reminders disabled never becomes progress
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
        db.NutritionFoodEntries.Add(Entry(dinner.Id)); await db.SaveChangesAsync();
        Assert.True(await db.NutritionLogAdditions.MaxAsync(x => x.Sequence) > initialSequence);
        await CreateAt(db, owner.Id, 3);
        Assert.Equal(2, await db.InAppNotifications.CountAsync());
        // A fresh service/context after a restart cannot create another logical slot notification.
        await using var restarted = factory.Services.CreateAsyncScope();
        await CreateAt(restarted.ServiceProvider.GetRequiredService<FitnessDbContext>(), owner.Id, 3);
        Assert.Equal(2, await db.InAppNotifications.CountAsync());
        Assert.Equal(3, await db.NotificationOccurrences.CountAsync());
    }

    [Fact]
    public async Task FilledMealWithoutNewFoodDoesNotSendAgainAfterEditing()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        db.NutritionMeals.AddRange(Meal(owner.Id, NutritionMealSlot.Breakfast), Meal(owner.Id, NutritionMealSlot.Lunch)); await db.SaveChangesAsync();
        await CreateAt(db, owner.Id, 1);
        await db.NutritionFoodEntries.ExecuteUpdateAsync(s => s.SetProperty(x => x.Grams, 200));
        await CreateAt(db, owner.Id, 2);
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
    }

    [Fact]
    public async Task TrainingUsesMovedOccurrenceAndCompletionAndIndependentCategoryPreferences()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var run = new RunningSession { Id = Guid.NewGuid(), Date = Day, Position = 0, Structure = "Rolig tur" };
        var plan = new RunningPlan { Id = Guid.NewGuid(), UserId = owner.Id, IsActive = true, Sessions = [run] };
        var workout = new ProgramWorkout { Id = Guid.NewGuid(), Name = "Styrke" };
        var program = new StrengthProgram { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Program", CreatedAt = Day.AddDays(-10).ToDateTime(TimeOnly.MinValue), Workouts = [workout] };
        program.Schedule.Add(new() { Id = Guid.NewGuid(), DayOfWeek = Day.AddDays(-1).DayOfWeek, WorkoutId = workout.Id });
        db.RunningPlans.Add(plan); db.StrengthPrograms.Add(program);
        db.CalendarOccurrenceMoves.AddRange(new CalendarOccurrenceMove { Id = Guid.NewGuid(), UserId = owner.Id, Kind = CalendarOccurrenceKind.Running, ScopeId = plan.Id, SourceId = run.Id, OriginalDate = Day, TargetDate = Day.AddDays(1) },
            new CalendarOccurrenceMove { Id = Guid.NewGuid(), UserId = owner.Id, Kind = CalendarOccurrenceKind.Strength, ScopeId = program.Id, SourceId = workout.Id, OriginalDate = Day.AddDays(-1), TargetDate = Day });
        await db.SaveChangesAsync(); await CreateAt(db, owner.Id, 0);
        var notice = Assert.Single(await db.InAppNotifications.ToArrayAsync());
        Assert.Contains($"/styrke/{program.Id}/træninger/{workout.Id}/aktiv?planlagt={Day.AddDays(-1):yyyy-MM-dd}", notice.TargetPath);
        // Completion is rechecked before dispatch, not merely when the inbox is created.
        db.CompletedWorkouts.Add(new() { Id = Guid.NewGuid(), UserId = owner.Id, CompletionId = Guid.NewGuid(), ProgramId = program.Id,
            WorkoutId = workout.Id, ScheduledOccurrenceDate = Day.AddDays(-1), WorkoutName = "Styrke", CompletedAt = At(0).UtcDateTime });
        var device = await AddDevice(factory, db, owner); db.NotificationDeliveries.Add(new() { NotificationId = notice.Id, SubscriptionId = device.Id, NextAttemptAtUtc = At(0).UtcDateTime }); await db.SaveChangesAsync();
        var sender = new RecordingPush(); await Scheduler(db, At(0), sender).DeliverAsync(default);
        Assert.Empty(sender.Payloads);
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
        // The moved run is available tomorrow, but its category can independently suppress it.
        db.UserSettings.Add(new() { UserId = owner.Id, RunningNotificationsEnabled = false, StrengthNotificationsEnabled = true }); await db.SaveChangesAsync();
        var tomorrow = At(0).AddDays(1);
        await Scheduler(db, tomorrow).CreateAsync(owner.Id, new(Day.AddDays(1), 0, tomorrow.UtcDateTime), default);
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
    }

    [Fact]
    public async Task PushRetriesKeepOneInboxRowAndStopAfterThreeAttempts()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await AddDevice(factory, db, owner); await CreateAt(db, owner.Id, 1);
        var sender = new RecordingPush { Result = PushDeliveryResult.Retry };
        for (var minute = 0; minute < 5; minute++) await Scheduler(db, At(1).AddMinutes(minute), sender).DeliverAsync(default);
        Assert.Equal(3, sender.Payloads.Count);
        Assert.All(sender.Payloads, p => Assert.Contains("Du har en ny besked", p));
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
        var delivery = Assert.Single(await db.NotificationDeliveries.AsNoTracking().ToArrayAsync());
        Assert.True(delivery.Finished); Assert.Equal(3, delivery.Attempts);
    }

    [Fact]
    public async Task ExpiredTokenDoesNotStopPushButLogoutOrLateDeliveryDoes()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var device = await AddDevice(factory, db, owner); await CreateAt(db, owner.Id, 1);
        var sender = new RecordingPush(); await Scheduler(db, At(1), sender).DeliverAsync(default);
        Assert.Single(sender.Payloads); // device session expired yesterday, but was not revoked
        await CreateAt(db, owner.Id, 2);
        await db.UserSessions.Where(x => x.Id == device.SessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        await Scheduler(db, At(2), sender).DeliverAsync(default);
        Assert.Single(sender.Payloads);
        await CreateAt(db, owner.Id, 3, At(3).AddMinutes(16));
        Assert.Equal(2, await db.InAppNotifications.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingMealReminderRechecksMealAndPreferenceWithoutCreatingProgress(bool fillMeal)
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        await AddDevice(factory, db, owner); await CreateAt(db, owner.Id, 1);
        if (fillMeal) db.NutritionMeals.Add(Meal(owner.Id, NutritionMealSlot.Breakfast));
        else db.UserSettings.Add(new() { UserId = owner.Id, MealRemindersEnabled = false });
        await db.SaveChangesAsync();
        var sender = new RecordingPush(); await Scheduler(db, At(1), sender).DeliverAsync(default);
        Assert.Empty(sender.Payloads);
        Assert.Equal(InAppNotificationKind.MealReminder, Assert.Single(await db.InAppNotifications.ToArrayAsync()).Kind);
    }

    [Fact]
    public async Task InboxReadCutoffAndDestinationsAreOwnerScoped()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved); var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = await Login(factory, owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var first = Notice(owner.Id); var foreign = Notice(other.Id); db.InAppNotifications.AddRange(first, foreign); await db.SaveChangesAsync();
        var loaded = (await client.GetFromJsonAsync<NotificationPageResponse>("api/notifications"))!;
        Assert.Equal(first.Id, Assert.Single(loaded.Items).Id);
        var later = Notice(owner.Id); db.InAppNotifications.Add(later); await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync("api/notifications/read", new MarkNotificationsReadRequest([first.Id, foreign.Id]));
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<NotificationCountResponse>())!.UnreadCount);
        db.ChangeTracker.Clear();
        Assert.NotNull((await db.InAppNotifications.SingleAsync(x => x.Id == first.Id)).ReadAtUtc);
        Assert.Null((await db.InAppNotifications.SingleAsync(x => x.Id == later.Id)).ReadAtUtc);
        Assert.Null((await db.InAppNotifications.SingleAsync(x => x.Id == foreign.Id)).ReadAtUtc);
        Assert.True((await client.GetFromJsonAsync<NotificationDestinationResponse>($"api/notifications/{foreign.Id}/destination"))!.Unavailable);
        using var anonymous = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/notifications/read", new MarkNotificationsReadRequest([first.Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/notifications/subscribe", new SubscribePushRequest("synthetic", "synthetic", "synthetic", "synthetic", "synthetic"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/notifications/test", new PushBindingRequest("synthetic"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("api/settings/notification-preferences", new UpdateNotificationPreferencesRequest(true, true, true, true, false, "Europe/Copenhagen"))).StatusCode);
    }

    [Fact]
    public async Task HistoricalAdminNoticesFollowCurrentRoleForAllInboxOperations()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        using var client = await Login(factory, owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var legacy = Notice(owner.Id); legacy.Kind = InAppNotificationKind.AdminRegistrationRequest;
        legacy.TargetPath = "/administration/registreringer";
        var ordinary = Notice(owner.Id);
        db.InAppNotifications.AddRange(legacy, ordinary); await db.SaveChangesAsync();
        Assert.Equal(2, (await client.GetFromJsonAsync<NotificationPageResponse>("api/notifications"))!.Items.Count);
        Assert.False((await client.GetFromJsonAsync<NotificationDestinationResponse>($"api/notifications/{legacy.Id}/destination"))!.Unavailable);
        // Keep the original Admin-bearing access token: validation must refresh its role from the server.
        await factory.RemoveRoleAsync(owner.Id, AuthenticationConstants.AdminRole);
        var page = (await client.GetFromJsonAsync<NotificationPageResponse>("api/notifications"))!;
        Assert.Equal(ordinary.Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.UnreadCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<NotificationCountResponse>("api/notifications/unread"))!.UnreadCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<SettingsOverviewResponse>("api/settings"))!.UnreadNotificationCount);
        var destination = (await client.GetFromJsonAsync<NotificationDestinationResponse>($"api/notifications/{legacy.Id}/destination"))!;
        Assert.True(destination.Unavailable);
        Assert.Equal("/indstillinger/notifikationer", destination.TargetPath);
        var read = await client.PostAsJsonAsync("api/notifications/read", new MarkNotificationsReadRequest([legacy.Id, ordinary.Id]));
        Assert.Equal(0, (await read.Content.ReadFromJsonAsync<NotificationCountResponse>())!.UnreadCount);
        db.ChangeTracker.Clear();
        Assert.Null((await db.InAppNotifications.SingleAsync(x => x.Id == legacy.Id)).ReadAtUtc);
        Assert.NotNull((await db.InAppNotifications.SingleAsync(x => x.Id == ordinary.Id)).ReadAtUtc);
        Assert.Equal(2, await db.InAppNotifications.CountAsync()); // History is retained, not deleted on role revocation.
    }

    [Fact]
    public async Task InstallationCleanupSurvivesClearedBindingAndNeverDeletesAnotherOwner()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = await Login(factory, owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var ownDevice = await AddDevice(factory, db, owner);
        var foreignDevice = await AddDevice(factory, db, other);
        foreignDevice.InstallationId = ownDevice.InstallationId; await db.SaveChangesAsync();
        var statusPath = "api/notifications/device?installationId=" + ownDevice.InstallationId;
        // After failed cleanup/reload there is no recipient binding, but the installation can still reconcile.
        var status = (await client.GetFromJsonAsync<PushDeviceResponse>(statusPath))!;
        Assert.False(status.Subscribed);
        Assert.True(status.HasRegistration);
        for (var retry = 0; retry < 2; retry++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/notifications/unsubscribe", new UnsubscribePushRequest(ownDevice.InstallationId))).StatusCode);
        Assert.False((await client.GetFromJsonAsync<PushDeviceResponse>(statusPath))!.HasRegistration);
        Assert.False(await db.WebPushSubscriptions.AnyAsync(x => x.UserId == owner.Id));
        Assert.True(await db.WebPushSubscriptions.AnyAsync(x => x.Id == foreignDevice.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/notifications/unsubscribe", new UnsubscribePushRequest("invalid"))).StatusCode);
        using var anonymous = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/notifications/unsubscribe", new UnsubscribePushRequest(foreignDevice.InstallationId))).StatusCode);
    }

    [Fact]
    public async Task DeviceOwnershipTestCooldownAndExpiryPreserveInbox()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved); var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var device = await AddDevice(factory, db, owner);
        var service = new NotificationService(db, new FixedClock(At(1)), Configured);
        await service.UnsubscribeAsync(other.Id, device.InstallationId, default);
        Assert.True(await db.WebPushSubscriptions.AnyAsync());
        Assert.Equal(PushSaveStatus.NotFound, await service.TestAsync(other.Id, device.Binding, default));
        Assert.Equal(PushSaveStatus.Saved, await service.TestAsync(owner.Id, device.Binding, default));
        Assert.Equal(PushSaveStatus.Limited, await service.TestAsync(owner.Id, device.Binding, default));
        var sender = new RecordingPush { Result = PushDeliveryResult.Expired }; await Scheduler(db, At(1), sender).DeliverAsync(default);
        Assert.False(await db.WebPushSubscriptions.AnyAsync());
        Assert.Single(await db.InAppNotifications.ToArrayAsync());
    }

    [Fact]
    public async Task RepeatedLoginAndSubscribeReplacesOnlyTheOwnersInstallation()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        db.UserSettings.Add(new() { UserId = owner.Id, RunningNotificationsEnabled = false, StrengthNotificationsEnabled = false,
            MealRemindersEnabled = false, NutritionProgressEnabled = false });
        var foreignDevice = await AddDevice(factory, db, other);
        var installation = foreignDevice.InstallationId; // The same browser can be used by different accounts.
        var service = new NotificationService(db, new FixedClock(At(1)), Configured);
        for (var login = 0; login < 12; login++)
        {
            var session = await factory.CreateSessionAsync(owner, At(0).AddDays(-1));
            var input = SubscriptionInput(installation);
            Assert.Equal(PushSaveStatus.Saved, await service.SubscribeAsync(owner.Id, session, input, default));
            var ownDevice = Assert.Single(await db.WebPushSubscriptions.AsNoTracking().Where(x => x.UserId == owner.Id).ToArrayAsync());
            Assert.Equal(input.Binding, ownDevice.Binding);
            Assert.Equal(input.Endpoint, ownDevice.Endpoint);
            Assert.Equal(session, ownDevice.SessionId);
        }
        Assert.True(await db.WebPushSubscriptions.AnyAsync(x => x.Id == foreignDevice.Id && x.UserId == other.Id));
        // Knowing an installation or recipient binding never permits taking over another owner's endpoint.
        var ownedSession = await factory.CreateSessionAsync(owner, At(0).AddDays(1));
        var attemptedTakeover = SubscriptionInput(installation) with { Endpoint = foreignDevice.Endpoint, Binding = foreignDevice.Binding };
        Assert.Equal(PushSaveStatus.Conflict, await service.SubscribeAsync(owner.Id, ownedSession, attemptedTakeover, default));
        Assert.Equal(2, await db.WebPushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task QuotaReclaimsRevokedAndStampInvalidDevicesButPreservesExpiredSessions()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var devices = new List<WebPushSubscription>();
        for (var i = 0; i < 10; i++) devices.Add(await AddDevice(factory, db, owner));
        var session = await factory.CreateSessionAsync(owner, At(0).AddDays(1));
        var service = new NotificationService(db, new FixedClock(At(1)), Configured);
        var input = SubscriptionInput(Guid.NewGuid().ToString("N"));
        // All existing sessions are expired but still valid for phone push, so quota stays full.
        Assert.Equal(PushSaveStatus.Limited, await service.SubscribeAsync(owner.Id, session, input, default));
        Assert.Equal(10, await db.WebPushSubscriptions.CountAsync());
        var revoked = devices.Take(4).Select(x => x.SessionId).ToArray();
        var stampInvalid = devices.Skip(4).Take(3).Select(x => x.SessionId).ToArray();
        await db.UserSessions.Where(x => revoked.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, At(0)));
        await db.UserSessions.Where(x => stampInvalid.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.SecurityStamp, "obsolete-stamp"));
        Assert.Equal(PushSaveStatus.Saved, await service.SubscribeAsync(owner.Id, session, input, default));
        Assert.Equal(4, await db.WebPushSubscriptions.CountAsync());
        var expiredStillValid = devices.Skip(7).Select(x => x.Id).ToArray();
        Assert.Equal(3, await db.WebPushSubscriptions.CountAsync(x => expiredStillValid.Contains(x.Id)));
    }

    private static SubscribePushInput SubscriptionInput(string installation) => new(
        "https://web.push.apple.com/" + Guid.NewGuid().ToString("N"),
        Convert.ToBase64String(new byte[65]).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        Guid.NewGuid().ToString("N"), installation);

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/synthetic", true)]
    [InlineData("https://web.push.apple.com/synthetic", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/synthetic", true)]
    [InlineData("http://fcm.googleapis.com/test", false)]
    [InlineData("https://fcm.googleapis.com.evil.test/test", false)]
    [InlineData("https://127.0.0.1/test", false)]
    [InlineData("https://fcm.googleapis.com:8443/test", false)]
    [InlineData("https://user@fcm.googleapis.com/test", false)]
    public void PushEndpointsHaveNarrowHttpsAllowlist(string endpoint, bool allowed) => Assert.Equal(allowed, PushEndpointPolicy.IsAllowed(endpoint));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("fd00::1")]
    public void PushCannotConnectToPrivateAddresses(string address) => Assert.False(PushEndpointPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("//evil.test")]
    [InlineData("https://evil.test")]
    [InlineData("/\\evil.test")]
    [InlineData("/%2f%2fevil.test")]
    public void LoginReturnPathRejectsExternalDestinations(string path) => Assert.Equal("/", SafeReturnPath.Validate(path));

    private static InAppNotification Notice(string owner) => new() { Id = Guid.NewGuid(), UserId = owner, SourceKey = Guid.NewGuid().ToString("N"),
        Title = "Besked", Message = "Syntetisk besked", TargetPath = "/kost?dato=2026-10-12", CreatedAtUtc = DateTime.UtcNow };
    private static NutritionMeal Meal(string owner, NutritionMealSlot slot)
    {
        var id = Guid.NewGuid(); return new() { Id = id, UserId = owner, Date = Day, Slot = slot, Entries = [Entry(id)] };
    }
    private static NutritionFoodEntry Entry(Guid mealId) => new() { Id = Guid.NewGuid(), MealId = mealId, Name = "Syntetisk mad", Grams = 100,
        EnergyKcalPer100g = 100, ProteinPer100g = 10, FatPer100g = 2, CarbohydratePer100g = 12, SugarPer100g = null };
    private static DateTimeOffset At(int slot) => new(Day.ToDateTime(slot switch { 0 => new(5, 0), 1 => new(8, 0), 2 => new(12, 0), _ => new(17, 30) }, DateTimeKind.Utc));
    private static NotificationScheduler Scheduler(FitnessDbContext db, DateTimeOffset now, RecordingPush? sender = null)
    {
        var clock = new FixedClock(now);
        return new(db, new CalendarService(db, clock), new NutritionService(db, clock), clock, sender ?? new(), Configured);
    }
    private static Task CreateAt(FitnessDbContext db, string owner, int slot, DateTimeOffset? now = null) =>
        Scheduler(db, now ?? At(slot)).CreateAsync(owner, new(Day, slot, At(slot).UtcDateTime), default);
    private static async Task<WebPushSubscription> AddDevice(AuthWebApplicationFactory factory, FitnessDbContext db, ApplicationUser owner)
    {
        var session = await factory.CreateSessionAsync(owner, At(0).AddDays(-1));
        var device = new WebPushSubscription { Id = Guid.NewGuid(), UserId = owner.Id, SessionId = session, Binding = Guid.NewGuid().ToString("N"), InstallationId = Guid.NewGuid().ToString("N"),
            Endpoint = "https://web.push.apple.com/" + Guid.NewGuid().ToString("N"), P256dh = "synthetic", Auth = "synthetic", CreatedAtUtc = At(0).UtcDateTime };
        db.WebPushSubscriptions.Add(device); await db.SaveChangesAsync(); return device;
    }
    private static async Task<HttpClient> Login(AuthWebApplicationFactory factory, ApplicationUser user)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword });
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken); return client;
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class RecordingPush : IWebPushSender
    {
        public List<string> Payloads { get; } = [];
        public PushDeliveryResult Result { get; set; } = PushDeliveryResult.Delivered;
        public Task<PushDeliveryResult> SendAsync(WebPushSubscription subscription, string payload, string topic, int ttl, CancellationToken ct)
        { Payloads.Add(payload); Assert.InRange(ttl, 1, 900); return Task.FromResult(Result); }
    }
}
