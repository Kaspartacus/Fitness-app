using FitnessApp.Domain.Diary;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Diary;

public sealed class DiaryCleanup(FitnessDbContext db, TimeProvider clock)
{
    public async Task RunDailyAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime; var today = DateOnly.FromDateTime(now);
        var state = await db.DiaryMaintenance.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (state?.CompletedOnUtc >= today) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.DiaryEntries.Where(x => x.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
        var cutoff = now - DiaryRules.InactivityTimeout;
        await db.DiaryGrants.Where(x => x.LastActivityUtc <= cutoff ||
            !db.UserSessions.Any(s => s.Id == x.SessionId && s.UserId == x.UserId && s.RevokedAt == null))
            .ExecuteDeleteAsync(ct);
        if (state is null) { state = new() { Id = 1 }; db.DiaryMaintenance.Add(state); }
        state.CompletedOnUtc = today;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
