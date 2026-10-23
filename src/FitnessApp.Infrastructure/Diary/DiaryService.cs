using System.Security.Cryptography;
using System.Text;
using FitnessApp.Application.Diary;
using FitnessApp.Domain.Diary;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Diary;

public sealed class DiaryService(FitnessDbContext db, TimeProvider clock, DiaryOperationLocks locks,
    DiaryCipher cipher, UserManager<ApplicationUser> users) : IDiaryService
{
    private readonly PasswordHasher<DiaryProtection> hasher = new();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<T> Serialized<T>(string owner, Func<Task<T>> operation, CancellationToken ct)
    {
        var gate = locks.For(owner);
        await gate.WaitAsync(ct);
        try { return await operation(); }
        finally { gate.Release(); }
    }
    private Task<DiaryProtection?> Protection(string owner, CancellationToken ct) =>
        db.DiaryProtections.SingleOrDefaultAsync(x => x.UserId == owner, ct);
    private async Task<(DateOnly Today, TimeZoneInfo Zone)> LocalDay(string owner, CancellationToken ct)
    {
        var zoneId = await db.UserSettings.Where(x => x.UserId == owner).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Copenhagen";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime), zone);
    }
    private static string Hash(string grant) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant)));
    private async Task<bool> Allowed(string owner, string session, string? grant, DiaryProtection? protection, CancellationToken ct)
    {
        if (protection is null) return false;
        if (protection.CodeHash is null) return true;
        if (grant is not { Length: 64 }) return false;
        var hash = Hash(grant); var cutoff = Now - DiaryRules.InactivityTimeout;
        return await db.DiaryGrants.AnyAsync(x => x.Hash == hash && x.UserId == owner && x.SessionId == session &&
            x.Revision == protection.Revision && x.LastActivityUtc > cutoff, ct);
    }
    public Task<DiaryResult<DiaryState>> StateAsync(string owner, string session, string? grant, CancellationToken ct) => Serialized(owner, async () =>
    {
        var protection = await Protection(owner, ct); var (today, zone) = await LocalDay(owner, ct);
        return new DiaryResult<DiaryState>(DiaryStatus.Success, new(protection is not null, protection?.CodeHash is not null,
            await Allowed(owner, session, grant, protection, ct), today, zone.Id)
        {
            MinimumNewEntryDate = DiaryRules.EarliestCreationDate(today)
        });
    }, ct);

    private bool TemporarilyLocked(DiaryProtection protection) => protection.LockedUntilUtc > Now;
    private async Task<DiaryStatus> CheckCode(DiaryProtection protection, string? code, CancellationToken ct)
    {
        if (TemporarilyLocked(protection)) return DiaryStatus.Limited;
        if (!DiaryRules.ValidCode(code) || hasher.VerifyHashedPassword(protection, protection.CodeHash!, code!) == PasswordVerificationResult.Failed)
            return await Failed(protection, ct);
        return DiaryStatus.Success;
    }
    private async Task<DiaryStatus> Failed(DiaryProtection protection, CancellationToken ct)
    {
        if (protection.LockedUntilUtc is not null && protection.LockedUntilUtc <= Now)
        {
            // Start one new attempt window after expiry, rather than resetting on every guess.
            protection.FailedAttempts = 0;
            protection.LockedUntilUtc = null;
        }
        protection.FailedAttempts++;
        if (protection.FailedAttempts >= 5) protection.LockedUntilUtc = Now.AddMinutes(5);
        await db.SaveChangesAsync(ct);
        return protection.FailedAttempts >= 5 ? DiaryStatus.Limited : DiaryStatus.Invalid;
    }
    private async Task<string> Grant(DiaryProtection protection, string session, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        // Browser tabs share a login session but retain independent unlock grants and inactivity deadlines.
        await db.DiaryGrants.Where(x => x.UserId == protection.UserId && x.LastActivityUtc <= Now - DiaryRules.InactivityTimeout).ExecuteDeleteAsync(ct);
        db.DiaryGrants.Add(new() { Hash = Hash(token), UserId = protection.UserId, SessionId = session,
            Revision = protection.Revision, LastActivityUtc = Now });
        protection.FailedAttempts = 0; protection.LockedUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return token;
    }
    public Task<DiaryResult<string?>> UnlockAsync(string owner, string session, string? code, CancellationToken ct) => Serialized(owner, async () =>
    {
        var protection = await Protection(owner, ct);
        if (protection?.CodeHash is null) return new DiaryResult<string?>(DiaryStatus.Invalid);
        var status = await CheckCode(protection, code, ct);
        if (status != DiaryStatus.Success) return new DiaryResult<string?>(status);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var token = await Grant(protection, session, ct);
        await transaction.CommitAsync(ct);
        return new DiaryResult<string?>(DiaryStatus.Success, token);
    }, ct);

    public Task<DiaryResult<string?>> ConfigureAsync(string owner, string session, string? currentCode, string? newCode, string? confirmation, CancellationToken ct) => Serialized(owner, async () =>
    {
        if (newCode is not null && (!DiaryRules.ValidCode(newCode) || newCode != confirmation)) return new DiaryResult<string?>(DiaryStatus.Invalid);
        var protection = await Protection(owner, ct);
        if (protection?.CodeHash is not null)
        {
            var checkedCode = await CheckCode(protection, currentCode, ct);
            if (checkedCode != DiaryStatus.Success) return new DiaryResult<string?>(checkedCode);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (protection is null) { protection = new() { UserId = owner }; db.DiaryProtections.Add(protection); }
        protection.CodeHash = newCode is null ? null : hasher.HashPassword(protection, newCode);
        protection.Revision = Guid.NewGuid(); protection.FailedAttempts = 0; protection.LockedUntilUtc = null;
        await db.DiaryGrants.Where(x => x.UserId == owner).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        // Changing protection revokes every grant, including this session. Unlock explicitly again.
        await transaction.CommitAsync(ct);
        return new DiaryResult<string?>(DiaryStatus.Success);
    }, ct);

    public Task<DiaryResult<bool>> RecoverAsync(string owner, string? accountPassword, CancellationToken ct) => Serialized(owner, async () =>
    {
        var protection = await Protection(owner, ct);
        if (protection is null) return new DiaryResult<bool>(DiaryStatus.Invalid);
        if (TemporarilyLocked(protection)) return new DiaryResult<bool>(DiaryStatus.Limited);
        var user = await users.FindByIdAsync(owner);
        // Verify a newly submitted password, never the existing login or diary grant.
        if (accountPassword is not { Length: >= 1 and <= 1024 } || user is null || !await users.CheckPasswordAsync(user, accountPassword))
            return new DiaryResult<bool>(await Failed(protection, ct));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        protection.CodeHash = null; protection.Revision = Guid.NewGuid(); protection.FailedAttempts = 0; protection.LockedUntilUtc = null;
        await db.DiaryGrants.Where(x => x.UserId == owner).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new DiaryResult<bool>(DiaryStatus.Success, true);
    }, ct);

    public Task<DiaryResult<bool>> ActivityAsync(string owner, string session, string? grant, CancellationToken ct) => Serialized(owner, async () =>
    {
        var protection = await Protection(owner, ct);
        if (!await Allowed(owner, session, grant, protection, ct)) return new DiaryResult<bool>(DiaryStatus.Locked);
        if (protection!.CodeHash is not null)
            await db.DiaryGrants.Where(x => x.UserId == owner && x.SessionId == session && x.Hash == Hash(grant!))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityUtc, Now), ct);
        return new DiaryResult<bool>(DiaryStatus.Success, true);
    }, ct);
    public async Task LockAsync(string owner, string session, string? grant, CancellationToken ct)
    {
        if (grant is not { Length: 64 } || grant.Any(character => !Uri.IsHexDigit(character))) return;
        var hash = Hash(grant);
        await Serialized(owner, async () =>
        {
            // A delayed close must not revoke a newer unlock issued for the same session.
            await db.DiaryGrants.Where(x => x.UserId == owner && x.SessionId == session && x.Hash == hash).ExecuteDeleteAsync(ct);
            return true;
        }, ct);
    }
    public Task<DiaryResult<IReadOnlyList<DiaryDate>>> DatesAsync(string owner, string session, string? grant, int year, int month, CancellationToken ct) => Serialized(owner, async () =>
    {
        if (!await Allowed(owner, session, grant, await Protection(owner, ct), ct)) return new DiaryResult<IReadOnlyList<DiaryDate>>(DiaryStatus.Locked);
        if (year is < 1 or > 9998 || month is < 1 or > 12) return new DiaryResult<IReadOnlyList<DiaryDate>>(DiaryStatus.Invalid);
        var first = new DateOnly(year, month, 1); var next = first.AddMonths(1);
        var rows = await db.DiaryEntries.AsNoTracking().Where(x => x.UserId == owner && x.Date >= first && x.Date < next && x.ExpiresAtUtc > Now)
            .OrderByDescending(x => x.Date).Select(x => new DiaryDate(x.Date, x.Version)).ToListAsync(ct);
        return new DiaryResult<IReadOnlyList<DiaryDate>>(DiaryStatus.Success, rows);
    }, ct);
    public Task<DiaryResult<DiaryEntryData>> ReadAsync(string owner, string session, string? grant, DateOnly date, CancellationToken ct) => Serialized(owner, async () =>
    {
        if (!await Allowed(owner, session, grant, await Protection(owner, ct), ct)) return new DiaryResult<DiaryEntryData>(DiaryStatus.Locked);
        var row = await db.DiaryEntries.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Date == date && x.ExpiresAtUtc > Now, ct);
        return row is null ? new DiaryResult<DiaryEntryData>(DiaryStatus.NotFound) : Decode(row);
    }, ct);
    private DiaryResult<DiaryEntryData> Decode(DiaryEntry row)
    {
        try { return new(DiaryStatus.Success, new(row.Date, cipher.Decrypt(row.UserId, row.Date, row.Ciphertext), row.Version)); }
        catch (CryptographicException) { return new(DiaryStatus.Unavailable); } // Never replace unreadable content or reveal provider messages.
    }
    public Task<DiaryResult<DiaryEntryData>> SaveAsync(string owner, string session, string? grant, DateOnly date, string? text, Guid? version, CancellationToken ct) => Serialized(owner, async () =>
    {
        if (!await Allowed(owner, session, grant, await Protection(owner, ct), ct)) return new DiaryResult<DiaryEntryData>(DiaryStatus.Locked);
        if (text is null || text.Length > DiaryRules.MaximumTextLength)
            return new DiaryResult<DiaryEntryData>(DiaryStatus.Invalid);
        var row = await db.DiaryEntries.SingleOrDefaultAsync(x => x.UserId == owner && x.Date == date, ct);
        if (row is not null && (row.Version != version || row.ExpiresAtUtc <= Now) || row is null && version is not null)
            return new DiaryResult<DiaryEntryData>(DiaryStatus.Conflict);
        if (row is not null && Decode(row).Status != DiaryStatus.Success) return new DiaryResult<DiaryEntryData>(DiaryStatus.Unavailable);
        if (row is null)
        {
            var (today, zone) = await LocalDay(owner, ct);
            if (date > today || DiaryRules.ExpiryDate(date) <= today)
                return new DiaryResult<DiaryEntryData>(DiaryStatus.Invalid);
            var expiry = DiaryRules.ExpiryDate(date).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // Midnight may be skipped in some timezones. Expire at the first valid local instant.
            while (zone.IsInvalidTime(expiry)) expiry = expiry.AddMinutes(1);
            row = new() { UserId = owner, Date = date, ExpiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(expiry, zone) };
            db.DiaryEntries.Add(row);
        }
        row.Ciphertext = cipher.Encrypt(owner, date, text); row.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new DiaryResult<DiaryEntryData>(DiaryStatus.Conflict); }
        return new DiaryResult<DiaryEntryData>(DiaryStatus.Success, new(date, text, row.Version));
    }, ct);
    public Task<DiaryResult<bool>> DeleteAsync(string owner, string session, string? grant, DateOnly date, Guid version, CancellationToken ct) => Serialized(owner, async () =>
    {
        if (!await Allowed(owner, session, grant, await Protection(owner, ct), ct)) return new DiaryResult<bool>(DiaryStatus.Locked);
        var count = await db.DiaryEntries.Where(x => x.UserId == owner && x.Date == date && x.Version == version && x.ExpiresAtUtc > Now).ExecuteDeleteAsync(ct);
        return new DiaryResult<bool>(count == 1 ? DiaryStatus.Success : DiaryStatus.Conflict, count == 1);
    }, ct);
}
