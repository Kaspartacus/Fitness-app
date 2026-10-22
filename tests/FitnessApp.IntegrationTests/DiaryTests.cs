using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FitnessApp.Application.Authentication;
using FitnessApp.Application.Diary;
using FitnessApp.Contracts.Diary;
using FitnessApp.Domain.Diary;
using FitnessApp.Domain.Settings;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Diary;
using FitnessApp.Infrastructure.Notifications;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryTests
{
    private const string SyntheticText = "Synthetic diary paragraph.\n\nAnother synthetic line.";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData("x")]
    [InlineData(" ")]
    [InlineData(" ! special ø 漢字 ")]
    public async Task ExactCodesAndSessionBoundGrantsProtectEveryContentOperation(string code)
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var first = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        var other = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Copenhagen").DateTime);
        await With(factory, async (service, _) =>
        {
            Assert.Equal(DiaryStatus.Success, (await service.ConfigureAsync(user.Id, first, null, code, code, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(user.Id, first, null, day, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.SaveAsync(user.Id, first, null, day, SyntheticText, null, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.DatesAsync(user.Id, first, null, day.Year, day.Month, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.DeleteAsync(user.Id, first, null, day, Guid.NewGuid(), Ct)).Status);
            Assert.Equal(DiaryStatus.Invalid, (await service.UnlockAsync(user.Id, first, code + "x", Ct)).Status);
            var unlocked = await service.UnlockAsync(user.Id, first, code, Ct);
            Assert.Equal(DiaryStatus.Success, unlocked.Status); Assert.NotNull(unlocked.Value);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(user.Id, other, unlocked.Value, day, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.SaveAsync(user.Id, first, unlocked.Value, day, SyntheticText, null, Ct)).Status);
            await service.LockAsync(user.Id, first, unlocked.Value, Ct);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(user.Id, first, unlocked.Value, day, Ct)).Status);
        });
    }

    [Fact]
    public async Task GrantsExpireAtFifteenMinutesAndReadsDoNotRenewThem()
    {
        var clock = new DiaryClock(DateTimeOffset.UtcNow);
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(user, clock.GetUtcNow().AddHours(1));
        string? grant = null; DateOnly day = default;
        await With(factory, async (service, _) =>
        {
            day = (await service.StateAsync(user.Id, session, null, Ct)).Value!.Today;
            await service.ConfigureAsync(user.Id, session, null, "x", "x", Ct);
            grant = (await service.UnlockAsync(user.Id, session, "x", Ct)).Value;
            await service.SaveAsync(user.Id, session, grant, day, SyntheticText, null, Ct);
        });
        clock.Advance(TimeSpan.FromMinutes(14));
        await With(factory, async (service, _) => Assert.Equal(DiaryStatus.Success, (await service.ReadAsync(user.Id, session, grant, day, Ct)).Status));
        clock.Advance(TimeSpan.FromMinutes(1));
        await With(factory, async (service, _) =>
        {
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(user.Id, session, grant, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(user.Id, session, grant, day, Ct)).Status);
            Assert.False((await service.StateAsync(user.Id, session, null, Ct)).Value!.Unlocked); // Reload has no credential.
        });
    }

    [Fact]
    public async Task ParallelFailuresPersistAcrossSessionsAndFreshPasswordRecoveryRevokesGrants()
    {
        var clock = new DiaryClock(DateTimeOffset.UtcNow);
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(user, clock.GetUtcNow().AddHours(1));
        var other = await factory.CreateSessionAsync(user, clock.GetUtcNow().AddHours(1));
        string? grant = null;
        await With(factory, async (service, _) =>
        {
            await service.ConfigureAsync(user.Id, session, null, "old", "old", Ct);
            grant = (await service.UnlockAsync(user.Id, session, "old", Ct)).Value;
        });
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => With(factory, async (service, _) =>
            await service.UnlockAsync(user.Id, other, "wrong", Ct))));
        await With(factory, async (service, db) =>
        {
            Assert.Equal(5, (await db.DiaryProtections.AsNoTracking().SingleAsync()).FailedAttempts);
            Assert.Equal(DiaryStatus.Limited, (await service.UnlockAsync(user.Id, session, "old", Ct)).Status);
            Assert.Equal(DiaryStatus.Limited, (await service.RecoverAsync(user.Id, factory.ValidPassword, Ct)).Status);
        });
        // A cooldown expiry starts one fresh window. Repeat across fresh contexts and sessions;
        // password-recovery guesses share the same persisted budget as code guesses.
        for (var cycle = 0; cycle < 2; cycle++)
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            await With(factory, async (service, _) => Assert.Equal(DiaryStatus.Invalid,
                (await service.RecoverAsync(user.Id, "wrong recovery password", Ct)).Status));
            await Task.WhenAll(Enumerable.Range(0, 4).Select(index => With(factory, async (service, _) =>
            {
                if (index % 2 == 0) await service.UnlockAsync(user.Id, other, "wrong code", Ct);
                else await service.RecoverAsync(user.Id, "wrong recovery password", Ct);
            })));
            await With(factory, async (service, db) =>
            {
                Assert.Equal(5, (await db.DiaryProtections.AsNoTracking().SingleAsync()).FailedAttempts);
                Assert.Equal(DiaryStatus.Limited, (await service.UnlockAsync(user.Id, session, "old", Ct)).Status);
                Assert.Equal(DiaryStatus.Limited, (await service.RecoverAsync(user.Id, factory.ValidPassword, Ct)).Status);
            });
        }
        clock.Advance(TimeSpan.FromMinutes(5));
        await With(factory, async (service, db) =>
        {
            Assert.Equal(DiaryStatus.Invalid, (await service.RecoverAsync(user.Id, "wrong password", Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.RecoverAsync(user.Id, factory.ValidPassword, Ct)).Status);
            Assert.Empty(await db.DiaryGrants.AsNoTracking().ToArrayAsync());
            await service.ConfigureAsync(user.Id, session, null, "new", "new", Ct);
            Assert.False((await service.StateAsync(user.Id, session, grant, Ct)).Value!.Unlocked);
            Assert.Equal(DiaryStatus.Invalid, (await service.UnlockAsync(user.Id, session, "old", Ct)).Status);
        });
    }

    [Fact]
    public async Task CodeChangesRequireCurrentCodeAndRevokeAllSessions()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        await With(factory, async (service, _) =>
        {
            Assert.Equal(DiaryStatus.Invalid, (await service.ConfigureAsync(user.Id, session, null, "", "", Ct)).Status);
            Assert.Equal(DiaryStatus.Invalid, (await service.ConfigureAsync(user.Id, session, null, new string('a', 129), new string('a', 129), Ct)).Status);
            await service.ConfigureAsync(user.Id, session, null, " x ", " x ", Ct);
            var grant = (await service.UnlockAsync(user.Id, session, " x ", Ct)).Value;
            Assert.Equal(DiaryStatus.Invalid, (await service.ConfigureAsync(user.Id, session, "x", null, null, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.ConfigureAsync(user.Id, session, " x ", "z", "z", Ct)).Status);
            Assert.False((await service.StateAsync(user.Id, session, grant, Ct)).Value!.Unlocked);
            Assert.Equal(DiaryStatus.Invalid, (await service.ConfigureAsync(user.Id, session, null, null, null, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.ConfigureAsync(user.Id, session, "z", null, null, Ct)).Status);
        });
    }

    [Fact]
    public async Task DelayedLockRevokesOnlyItsSuppliedGrantWithinTheOwnerAndSession()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var other = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(owner, DateTimeOffset.UtcNow.AddHours(1));
        var otherSession = await factory.CreateSessionAsync(owner, DateTimeOffset.UtcNow.AddHours(1));
        var otherOwnerSession = await factory.CreateSessionAsync(other, DateTimeOffset.UtcNow.AddHours(1));
        await With(factory, async (service, _) =>
        {
            await service.ConfigureAsync(owner.Id, session, null, "x", "x", Ct);
            await service.ConfigureAsync(other.Id, otherOwnerSession, null, "y", "y", Ct);
            var oldGrant = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            var newGrant = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            var otherSessionGrant = (await service.UnlockAsync(owner.Id, otherSession, "x", Ct)).Value!;
            var otherOwnerGrant = (await service.UnlockAsync(other.Id, otherOwnerSession, "y", Ct)).Value!;
            var today = (await service.StateAsync(owner.Id, session, newGrant, Ct)).Value!.Today;

            await service.LockAsync(other.Id, session, newGrant, Ct);
            await service.LockAsync(owner.Id, otherSession, newGrant, Ct);
            await service.LockAsync(owner.Id, session, null, Ct);
            await service.LockAsync(owner.Id, session, "malformed", Ct);
            await service.LockAsync(owner.Id, session, new string('z', 64), Ct);
            // The original page's close reaches the server after this session re-unlocks.
            await service.LockAsync(owner.Id, session, oldGrant, Ct);

            Assert.Equal(DiaryStatus.Success, (await service.SaveAsync(owner.Id, session, newGrant, today, SyntheticText, null, Ct)).Status);
            var read = await service.ReadAsync(owner.Id, session, newGrant, today, Ct);
            Assert.Equal(DiaryStatus.Success, read.Status); Assert.Equal(SyntheticText, read.Value!.Text);
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(owner.Id, session, newGrant, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(owner.Id, session, oldGrant, today, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(owner.Id, session, oldGrant, Ct)).Status);

            await service.LockAsync(owner.Id, session, newGrant, Ct);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(owner.Id, session, newGrant, today, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(owner.Id, session, newGrant, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(owner.Id, otherSession, otherSessionGrant, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(other.Id, otherOwnerSession, otherOwnerGrant, Ct)).Status);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSessionTabsKeepIndependentGrantsUntilCloseOrAccountWideProtectionChange(bool recover)
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-10-22T10:00:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(owner, clock.Now.AddHours(1));
        await With(factory, async (service, db) =>
        {
            await service.ConfigureAsync(owner.Id, session, null, "x", "x", Ct);
            var first = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            var today = (await service.StateAsync(owner.Id, session, first, Ct)).Value!.Today;
            var original = await service.SaveAsync(owner.Id, session, first, today, SyntheticText, null, Ct);
            Assert.Equal(DiaryStatus.Success, original.Status);
            var second = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            Assert.NotEqual(first, second);
            var firstRead = await service.ReadAsync(owner.Id, session, first, today, Ct);
            var secondRead = await service.ReadAsync(owner.Id, session, second, today, Ct);
            Assert.Equal(DiaryStatus.Success, firstRead.Status); Assert.Equal(SyntheticText, firstRead.Value!.Text);
            Assert.Equal(DiaryStatus.Success, secondRead.Status); Assert.Equal(SyntheticText, secondRead.Value!.Text);
            var edited = await service.SaveAsync(owner.Id, session, first, today, "Synthetic other-tab update", original.Value!.Version, Ct);
            Assert.Equal(DiaryStatus.Success, edited.Status);
            Assert.Equal(DiaryStatus.Conflict, (await service.SaveAsync(owner.Id, session, second, today,
                "Synthetic preserved stale draft", original.Value.Version, Ct)).Status);

            await service.LockAsync(owner.Id, session, first, Ct);
            Assert.Equal(DiaryStatus.Locked, (await service.ReadAsync(owner.Id, session, first, today, Ct)).Status);
            Assert.Equal("Synthetic other-tab update", (await service.ReadAsync(owner.Id, session, second, today, Ct)).Value!.Text);
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(owner.Id, session, second, Ct)).Status);
            var third = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            Assert.Equal(2, await db.DiaryGrants.AsNoTracking().CountAsync());

            if (recover)
                Assert.Equal(DiaryStatus.Success, (await service.RecoverAsync(owner.Id, factory.ValidPassword, Ct)).Status);
            else
                Assert.Equal(DiaryStatus.Success, (await service.ConfigureAsync(owner.Id, session, "x", "y", "y", Ct)).Status);
            Assert.Empty(await db.DiaryGrants.AsNoTracking().ToArrayAsync());
            if (recover) await service.ConfigureAsync(owner.Id, session, null, "y", "y", Ct);
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(owner.Id, session, second, Ct)).Status);
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(owner.Id, session, third, Ct)).Status);
        });
    }

    [Fact]
    public async Task AnotherSameSessionTabUnlockAndActivityDoNotRenewAnOlderGrant()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-10-22T10:00:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var session = await factory.CreateSessionAsync(owner, clock.Now.AddHours(1));
        await With(factory, async (service, db) =>
        {
            await service.ConfigureAsync(owner.Id, session, null, "x", "x", Ct);
            var first = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            clock.Advance(TimeSpan.FromMinutes(14));
            var second = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(owner.Id, session, second, Ct)).Status);
            Assert.True((await service.StateAsync(owner.Id, session, first, Ct)).Value!.Unlocked);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(DiaryStatus.Locked, (await service.ActivityAsync(owner.Id, session, first, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.ActivityAsync(owner.Id, session, second, Ct)).Status);
            var third = (await service.UnlockAsync(owner.Id, session, "x", Ct)).Value!;
            Assert.Equal(2, await db.DiaryGrants.AsNoTracking().CountAsync()); // Only the expired first grant is pruned.
            Assert.True((await service.StateAsync(owner.Id, session, second, Ct)).Value!.Unlocked);
            Assert.True((await service.StateAsync(owner.Id, session, third, Ct)).Value!.Unlocked);
        });
    }

    [Fact]
    public async Task CiphertextSurvivesProviderRestartIsBoundToOwnerDateAndRejectsSilentOverwrite()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await With(factory, async (service, db) =>
        {
            var day = (await service.StateAsync(user.Id, "unused", null, Ct)).Value!.Today;
            await service.ConfigureAsync(user.Id, "unused", null, null, null, Ct);
            var saved = await service.SaveAsync(user.Id, "unused", null, day, SyntheticText, null, Ct);
            var row = await db.DiaryEntries.AsNoTracking().SingleAsync();
            Assert.DoesNotContain("Synthetic", row.Ciphertext);
            var restarted = new DiaryCipher(DataProtectionProvider.Create(new DirectoryInfo(factory.Storage.KeyRingPath), b => b.SetApplicationName("FitnessApp")));
            Assert.Equal(SyntheticText, restarted.Decrypt(user.Id, day, row.Ciphertext));
            Assert.Throws<CryptographicException>(() => restarted.Decrypt("another-owner", day, row.Ciphertext));
            Assert.Throws<CryptographicException>(() => restarted.Decrypt(user.Id, day.AddDays(-1), row.Ciphertext));
            Assert.Equal(DiaryStatus.Conflict, (await service.SaveAsync(user.Id, "unused", null, day, "other", null, Ct)).Status);
            Assert.Equal(DiaryStatus.Invalid, (await service.SaveAsync(user.Id, "unused", null, day, new string('x', 12001), saved.Value!.Version, Ct)).Status);
            var edited = await service.SaveAsync(user.Id, "unused", null, day, "updated synthetic", saved.Value!.Version, Ct);
            Assert.Equal(DiaryStatus.Success, edited.Status);
            Assert.Equal(DiaryStatus.Conflict, (await service.DeleteAsync(user.Id, "unused", null, day, saved.Value.Version, Ct)).Status);
        });
    }

    [Fact]
    public async Task OwnershipAndAdminIsolationAreEnforcedAtHttpBoundary()
    {
        using var factory = new AuthWebApplicationFactory(); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var admin = await factory.CreateUserAsync(AccountApprovalStatus.Approved, AuthenticationConstants.AdminRole);
        using var a = await Client(factory, owner); using var b = await Client(factory, admin);
        var state = (await a.GetFromJsonAsync<DiaryStatusResponse>("api/diary/state"))!;
        (await a.PutAsJsonAsync("api/diary/protection", new ConfigureDiaryRequest(null, null, null))).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("api/diary/protection", new ConfigureDiaryRequest(null, null, null))).EnsureSuccessStatusCode();
        var save = await a.PutAsJsonAsync($"api/diary/entries/{state.Today:yyyy-MM-dd}", new SaveDiaryRequest(SyntheticText, null)); save.EnsureSuccessStatusCode();
        Assert.True(save.Headers.CacheControl?.NoStore); Assert.True(save.Headers.CacheControl?.Private);
        var entry = (await save.Content.ReadFromJsonAsync<DiaryEntryResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"api/diary/entries/{state.Today:yyyy-MM-dd}?userId={owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.DeleteAsync($"api/diary/entries/{state.Today:yyyy-MM-dd}/{entry.Version}")).StatusCode);
        Assert.Equal(SyntheticText, (await a.GetFromJsonAsync<DiaryEntryResponse>($"api/diary/entries/{state.Today:yyyy-MM-dd}"))!.Text);
    }

    [Fact]
    public async Task AnniversaryExpiryIsFixedEditingCannotExtendAndDailyCleanupResumes()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-03-01T10:00:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var day = new DateOnly(2025, 3, 2); Guid version = default; DateTime expiry = default;
        await With(factory, async (service, db) =>
        {
            await service.ConfigureAsync(user.Id, "unused", null, null, null, Ct);
            var saved = await service.SaveAsync(user.Id, "unused", null, day, SyntheticText, null, Ct); version = saved.Value!.Version;
            expiry = (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc;
            await service.SaveAsync(user.Id, "unused", null, day, "edited synthetic", version, Ct);
            Assert.Equal(expiry, (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc);
        });
        clock.Now = new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc));
        await With(factory, async (service, db) =>
        {
            Assert.Equal(DiaryStatus.NotFound, (await service.ReadAsync(user.Id, "unused", null, day, Ct)).Status);
            Assert.Empty((await service.DatesAsync(user.Id, "unused", null, day.Year, day.Month, Ct)).Value!);
            await new DiaryCleanup(db, clock).RunDailyAsync(Ct);
            Assert.Empty(await db.DiaryEntries.ToArrayAsync());
        });
        await With(factory, async (_, db) =>
        { await new DiaryCleanup(db, clock).RunDailyAsync(Ct); Assert.Single(await db.DiaryMaintenance.ToArrayAsync()); });
        Assert.Equal(new DateOnly(2025, 2, 28), DiaryRules.ExpiryDate(new(2024, 2, 29)));
    }

    [Fact]
    public async Task TimezoneChangeCannotMakeALiveEntryUneditableOrExtendItsFixedExpiry()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-03-01T10:00:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var day = new DateOnly(2025, 3, 2); Guid version = default; DateTime expiry = default;
        await With(factory, async (service, db) =>
        {
            await service.ConfigureAsync(owner.Id, "unused", null, null, null, Ct);
            var saved = await service.SaveAsync(owner.Id, "unused", null, day, SyntheticText, null, Ct);
            Assert.Equal(DiaryStatus.Success, saved.Status); version = saved.Value!.Version;
            expiry = (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc;
            Assert.Equal(new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc), expiry);
            db.UserSettings.Add(new() { UserId = owner.Id, TimeZoneId = "Asia/Tokyo" });
            await db.SaveChangesAsync();
        });
        clock.Now = DateTimeOffset.Parse("2026-03-01T18:00:00Z"); // Already March 2 in Tokyo; five retained hours remain.
        await With(factory, async (service, db) =>
        {
            Assert.Equal(DiaryStatus.Success, (await service.ReadAsync(owner.Id, "unused", null, day, Ct)).Status);
            Assert.Equal(day, Assert.Single((await service.DatesAsync(owner.Id, "unused", null, day.Year, day.Month, Ct)).Value!).Date);
            Assert.Equal(DiaryStatus.Invalid, (await service.SaveAsync(owner.Id, "unused", null, day, null, version, Ct)).Status);
            Assert.Equal(DiaryStatus.Invalid, (await service.SaveAsync(owner.Id, "unused", null, day, new string('x', DiaryRules.MaximumTextLength + 1), version, Ct)).Status);
            var saved = await service.SaveAsync(owner.Id, "unused", null, day, "Synthetic edit before original expiry", version, Ct);
            Assert.Equal(DiaryStatus.Success, saved.Status); version = saved.Value!.Version;
            Assert.Equal(expiry, (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc);
        });
        clock.Now = new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc));
        await With(factory, async (service, db) =>
        {
            Assert.Equal(DiaryStatus.NotFound, (await service.ReadAsync(owner.Id, "unused", null, day, Ct)).Status);
            Assert.Empty((await service.DatesAsync(owner.Id, "unused", null, day.Year, day.Month, Ct)).Value!);
            Assert.Equal(DiaryStatus.Conflict, (await service.SaveAsync(owner.Id, "unused", null, day, SyntheticText, version, Ct)).Status);
            Assert.Equal(DiaryStatus.Conflict, (await service.SaveAsync(owner.Id, "unused", null, day, SyntheticText, null, Ct)).Status);
            Assert.Equal(expiry, (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc);
            await new DiaryCleanup(db, clock).RunDailyAsync(Ct);
            Assert.Equal(DiaryStatus.Invalid, (await service.SaveAsync(owner.Id, "unused", null, day, SyntheticText, null, Ct)).Status);
            Assert.Empty(await db.DiaryEntries.AsNoTracking().ToArrayAsync());
        });
    }

    [Fact]
    public async Task WestwardTimezoneChangeKeepsExistingLocallyFutureEntryEditableButRejectsNewFutureDates()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-10-21T22:30:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var day = new DateOnly(2026, 10, 22); Guid version = default; DateTime expiry = default;
        await With(factory, async (service, db) =>
        {
            await service.ConfigureAsync(owner.Id, "unused", null, null, null, Ct);
            var saved = await service.SaveAsync(owner.Id, "unused", null, day, SyntheticText, null, Ct);
            Assert.Equal(DiaryStatus.Success, saved.Status); version = saved.Value!.Version;
            expiry = (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc;
            db.UserSettings.Add(new() { UserId = owner.Id, TimeZoneId = "America/Los_Angeles" });
            await db.SaveChangesAsync();
        });
        await With(factory, async (service, db) =>
        {
            Assert.Equal(day.AddDays(-1), (await service.StateAsync(owner.Id, "unused", null, Ct)).Value!.Today);
            Assert.Equal(DiaryStatus.Success, (await service.ReadAsync(owner.Id, "unused", null, day, Ct)).Status);
            Assert.Equal(DiaryStatus.Success, (await service.SaveAsync(owner.Id, "unused", null, day, "Synthetic westward edit", version, Ct)).Status);
            Assert.Equal(expiry, (await db.DiaryEntries.AsNoTracking().SingleAsync()).ExpiresAtUtc);
            Assert.Equal(DiaryStatus.Invalid, (await service.SaveAsync(owner.Id, "unused", null, day.AddDays(1), SyntheticText, null, Ct)).Status);
            Assert.Single(await db.DiaryEntries.AsNoTracking().ToArrayAsync());
        });
    }

    [Fact]
    public async Task ReminderUsesTimezoneSuppressesSavedTextAndReusesPersistedOccurrence()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-10-21T18:00:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var owner = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var due = Assert.Single(NotificationSchedule.Due(clock.Now, "Europe/Copenhagen")); Assert.Equal(4, due.Slot);
        await With(factory, async (service, db) =>
        {
            db.UserSettings.Add(new() { UserId = owner.Id, DiaryRemindersEnabled = true }); await db.SaveChangesAsync();
            await service.ConfigureAsync(owner.Id, "unused", null, null, null, Ct);
            var saved = await service.SaveAsync(owner.Id, "unused", null, due.Date, SyntheticText, null, Ct);
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().CreateAsync(owner.Id, due, Ct);
            Assert.Empty(await db.InAppNotifications.ToArrayAsync());
            Assert.Single(await db.NotificationOccurrences.ToArrayAsync());
            await service.SaveAsync(owner.Id, "unused", null, due.Date, " \n\t", saved.Value!.Version, Ct);
            await db.NotificationOccurrences.ExecuteDeleteAsync(); // A fresh due occurrence with whitespace is eligible.
        });
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().CreateAsync(owner.Id, due, Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().CreateAsync(owner.Id, due, Ct);
        await With(factory, async (_, db) =>
        {
            var notification = Assert.Single(await db.InAppNotifications.ToArrayAsync());
            Assert.Equal(InAppNotificationKind.DiaryReminder, notification.Kind);
            Assert.Equal("/dagbog?iDag=true", notification.TargetPath); Assert.DoesNotContain("Synthetic", notification.Message);
            Assert.True(NotificationService.IsSafePath(notification.TargetPath));
        });
        Assert.False(DiaryRules.ShouldRemind(false, false));
        Assert.False(DiaryRules.ShouldRemind(true, true));
    }

    [Fact]
    public async Task ServerTodayFollowsMidnightAndChangedTimezone()
    {
        var clock = new DiaryClock(DateTimeOffset.Parse("2026-10-21T21:59:00Z"));
        using var factory = new AuthWebApplicationFactory(timeProvider: clock); await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        await With(factory, async (service, _) => Assert.Equal(new DateOnly(2026, 10, 21),
            (await service.StateAsync(user.Id, "unused", null, Ct)).Value!.Today));
        clock.Advance(TimeSpan.FromMinutes(2));
        await With(factory, async (service, db) =>
        {
            Assert.Equal(new DateOnly(2026, 10, 22), (await service.StateAsync(user.Id, "unused", null, Ct)).Value!.Today);
            db.UserSettings.Add(new() { UserId = user.Id, TimeZoneId = "America/New_York" }); await db.SaveChangesAsync();
            var state = (await service.StateAsync(user.Id, "unused", null, Ct)).Value!;
            Assert.Equal(new DateOnly(2026, 10, 21), state.Today); Assert.Equal("America/New_York", state.TimeZoneId);
        });
    }

    private static async Task With(AuthWebApplicationFactory factory, Func<IDiaryService, FitnessDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IDiaryService>(), scope.ServiceProvider.GetRequiredService<FitnessDbContext>());
    }
    private static async Task<HttpClient> Client(AuthWebApplicationFactory factory, ApplicationUser user)
    {
        var client = factory.CreateHttpsClient();
        var session = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(user, session, DateTimeOffset.UtcNow.AddMinutes(10)));
        return client;
    }
    private sealed class DiaryClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan amount) => Now += amount;
    }
}
