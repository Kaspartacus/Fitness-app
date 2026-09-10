using FitnessApp.Application.Running;
using FitnessApp.Domain.Running;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Running;

public sealed class RunningService(FitnessDbContext db, TimeProvider timeProvider) : IRunningService
{
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();
    private static readonly DateOnly EarliestResultDate = new(2000, 1, 1);
    private static readonly DayOfWeek[] WeekdayOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public async Task<RunningOverviewData> GetOverviewAsync(string userId, CancellationToken cancellationToken)
    {
        var activePlan = await QueryPlans().Where(plan => plan.UserId == userId && plan.IsActive)
            .OrderByDescending(plan => plan.CreatedAtUtc).ThenBy(plan => plan.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var activePlanData = activePlan is null ? null : await MapPlanAsync(activePlan, cancellationToken);
        var resultEntities = await db.RunningResults.AsNoTracking().Where(result => result.UserId == userId)
            .OrderByDescending(result => result.Date).ThenByDescending(result => result.UpdatedAtUtc).ThenByDescending(result => result.Id)
            .ToListAsync(cancellationToken);
        var results = resultEntities.Select(Map).ToArray();
        var today = Today();
        var upcoming = activePlanData?.Sessions
            .Where(session => session.State != RunningSessionState.Completed &&
                (session.Date >= today || session.StartedAtUtc is not null))
            .OrderBy(session => session.Date).ThenBy(session => session.Id).ToArray() ?? [];
        return new RunningOverviewData(activePlanData, upcoming, results);
    }

    public async Task<RunningPlanData?> GetPlanAsync(string userId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await QueryPlans().SingleOrDefaultAsync(plan => plan.Id == planId && plan.UserId == userId,
            cancellationToken);
        return plan is null ? null : await MapPlanAsync(plan, cancellationToken);
    }

    public async Task<RunningPlanResult> CreatePlanAsync(string userId, RunningPlanInput input,
        CancellationToken cancellationToken)
    {
        var today = Today();
        if (!IsValidPlanInput(userId, input, today))
        {
            return new RunningPlanResult(RunningStatus.Invalid);
        }

        if (await ActivePlanExistsAsync(userId, cancellationToken))
        {
            return new RunningPlanResult(RunningStatus.ReplacementRequired);
        }

        var plan = new RunningPlanGenerator().Generate(userId, input, today, UtcNow());
        if (plan is null)
        {
            return new RunningPlanResult(RunningStatus.Infeasible);
        }

        db.RunningPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new RunningPlanResult(RunningStatus.Saved, await MapPlanAsync(plan, cancellationToken));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new RunningPlanResult(await ActivePlanExistsAsync(userId, cancellationToken)
                ? RunningStatus.ReplacementRequired
                : RunningStatus.Conflict);
        }
    }

    public async Task<RunningPlanResult> ReplacePlanAsync(string userId, Guid activePlanId,
        RunningPlanReplacementInput input, CancellationToken cancellationToken)
    {
        if (input is null || !input.ReplaceActivePlan)
        {
            return new RunningPlanResult(RunningStatus.ReplacementRequired);
        }

        var today = Today();
        if (input.Version == Guid.Empty || !IsValidPlanInput(userId, input.Plan, today))
        {
            return new RunningPlanResult(RunningStatus.Invalid);
        }

        var activePlan = await db.RunningPlans.AsNoTracking().SingleOrDefaultAsync(plan =>
            plan.Id == activePlanId && plan.UserId == userId, cancellationToken);
        if (activePlan is null)
        {
            return new RunningPlanResult(RunningStatus.NotFound);
        }

        if (!activePlan.IsActive || activePlan.Version != input.Version)
        {
            return new RunningPlanResult(RunningStatus.Conflict);
        }

        var hasStartedSession = await db.RunningSessions.AsNoTracking().Where(session =>
                session.PlanId == activePlanId && session.StartedAtUtc != null)
            .AnyAsync(session => !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == session.Id),
                cancellationToken);
        if (hasStartedSession)
        {
            return new RunningPlanResult(RunningStatus.Conflict);
        }

        var replacement = new RunningPlanGenerator().Generate(userId, input.Plan, today, UtcNow());
        if (replacement is null)
        {
            return new RunningPlanResult(RunningStatus.Infeasible);
        }

        var now = UtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.RunningPlans.Where(plan => plan.Id == activePlanId && plan.UserId == userId &&
                plan.IsActive && plan.Version == input.Version &&
                !db.RunningSessions.Any(session => session.PlanId == plan.Id && session.StartedAtUtc != null &&
                    !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == session.Id)))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(plan => plan.IsActive, false)
                .SetProperty(plan => plan.ReplacedAtUtc, now), cancellationToken);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningPlanResult(RunningStatus.Conflict);
        }

        db.RunningPlans.Add(replacement);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RunningPlanResult(RunningStatus.Saved, await MapPlanAsync(replacement, cancellationToken));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new RunningPlanResult(RunningStatus.Conflict);
        }
    }

    public async Task<RunningSessionListResult> ListSessionsAsync(string userId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to || to.DayNumber - from.DayNumber + 1 > RunningRules.MaxCalendarRangeDays)
        {
            return new RunningSessionListResult(RunningStatus.Invalid, []);
        }

        var activePlan = await QueryPlans().Where(plan => plan.UserId == userId && plan.IsActive)
            .OrderByDescending(plan => plan.CreatedAtUtc).ThenBy(plan => plan.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activePlan is null)
        {
            return new RunningSessionListResult(RunningStatus.Saved, []);
        }

        var sessions = activePlan.Sessions.Where(session => session.Date >= from && session.Date <= to)
            .OrderBy(session => session.Date).ThenBy(session => session.Position).ToArray();
        var resultBySession = await ResultsBySessionAsync(userId, sessions.Select(session => session.Id), cancellationToken);
        return new RunningSessionListResult(RunningStatus.Saved,
            sessions.Select(session => Map(session, resultBySession.GetValueOrDefault(session.Id))).ToArray());
    }

    public async Task<RunningSessionData?> GetSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var result = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.UserId == userId && candidate.SessionId == sessionId, cancellationToken);
        return Map(session, result);
    }

    public async Task<RunningSessionResult> StartSessionAsync(string userId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await FindOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return new RunningSessionResult(RunningStatus.NotFound);
        }

        if (session.Date > Today())
        {
            return new RunningSessionResult(RunningStatus.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockActivePlanAsync(userId, session.PlanId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Conflict);
        }

        var completed = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.UserId == userId && candidate.SessionId == sessionId, cancellationToken);
        if (completed is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Conflict, Map(session, completed));
        }

        var activeSession = await db.RunningSessions.AsNoTracking().Where(candidate =>
                candidate.PlanId == session.PlanId && candidate.StartedAtUtc != null &&
                !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == candidate.Id))
            .OrderBy(candidate => candidate.StartedAtUtc).ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeSession is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return activeSession.Id == sessionId
                ? new RunningSessionResult(RunningStatus.Saved, Map(activeSession))
                : new RunningSessionResult(RunningStatus.Conflict, Map(activeSession));
        }

        var now = UtcNow();
        var affected = await db.RunningSessions.Where(candidate => candidate.Id == sessionId &&
                candidate.PlanId == session.PlanId && candidate.StartedAtUtc == null &&
                !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == candidate.Id))
            .ExecuteUpdateAsync(updates => updates.SetProperty(candidate => candidate.StartedAtUtc, now), cancellationToken);
        if (affected == 1)
        {
            session.StartedAtUtc = now;
            await transaction.CommitAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Saved, Map(session));
        }

        await transaction.RollbackAsync(cancellationToken);
        return await CurrentSessionResultAsync(userId, sessionId, cancellationToken);
    }

    public async Task<RunningSessionResult> CancelSessionAsync(string userId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await FindOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return new RunningSessionResult(RunningStatus.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockActivePlanAsync(userId, session.PlanId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Conflict);
        }

        var completed = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.UserId == userId && candidate.SessionId == sessionId, cancellationToken);
        if (completed is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Conflict, Map(session, completed));
        }

        var affected = await db.RunningSessions.Where(candidate => candidate.Id == sessionId &&
                candidate.StartedAtUtc != null &&
                candidate.PlanId == session.PlanId &&
                !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == candidate.Id))
            .ExecuteUpdateAsync(updates => updates.SetProperty(candidate => candidate.StartedAtUtc, (DateTime?)null), cancellationToken);
        if (affected == 1)
        {
            session.StartedAtUtc = null;
            await transaction.CommitAsync(cancellationToken);
            return new RunningSessionResult(RunningStatus.Saved, Map(session));
        }

        var current = await GetSessionAsync(userId, sessionId, cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        if (current is null)
        {
            return new RunningSessionResult(RunningStatus.NotFound);
        }

        return current.State == RunningSessionState.Completed
            ? new RunningSessionResult(RunningStatus.Conflict, current)
            : new RunningSessionResult(RunningStatus.Saved, current);
    }

    public async Task<RunningResultOperationResult> CreateManualResultAsync(string userId,
        ManualRunningResultInput input, CancellationToken cancellationToken)
    {
        if (input is null || input.CompletionId == Guid.Empty ||
            !IsValidResult(input.Date, input.DistanceKm, input.DurationSeconds, input.AverageHeartRate, input.Note,
                requireDistanceAndDuration: true))
        {
            return new RunningResultOperationResult(RunningStatus.Invalid);
        }

        var existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
        if (existing is not null)
        {
            return new RunningResultOperationResult(MatchesManualResult(existing, input)
                ? RunningStatus.Saved
                : RunningStatus.Conflict, Map(existing));
        }

        var now = UtcNow();
        var result = new RunningResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompletionId = input.CompletionId,
            Date = input.Date,
            DistanceKm = input.DistanceKm,
            DurationSeconds = input.DurationSeconds,
            AverageHeartRate = input.AverageHeartRate,
            Note = NormalizeNote(input.Note),
            Version = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.RunningResults.Add(result);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new RunningResultOperationResult(RunningStatus.Saved, Map(result));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
            return new RunningResultOperationResult(existing is not null && MatchesManualResult(existing, input)
                ? RunningStatus.Saved
                : RunningStatus.Conflict, existing is null ? null : Map(existing));
        }
    }

    public async Task<RunningResultOperationResult> CompleteSessionAsync(string userId, Guid sessionId,
        CompleteRunningSessionInput input, CancellationToken cancellationToken)
    {
        if (input is null || input.CompletionId == Guid.Empty)
        {
            return new RunningResultOperationResult(RunningStatus.Invalid);
        }

        var existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
        if (existing is not null)
        {
            return new RunningResultOperationResult(MatchesSessionCompletion(existing, sessionId, input)
                ? RunningStatus.Saved
                : RunningStatus.Conflict, Map(existing));
        }

        var session = await FindOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return new RunningResultOperationResult(RunningStatus.NotFound);
        }

        var today = Today();
        var resultDate = input.Date ?? today;
        if (session.Date > today || resultDate < session.Date ||
            !IsValidResult(resultDate, input.DistanceKm, input.DurationSeconds, input.AverageHeartRate, input.Note,
                requireDistanceAndDuration: false))
        {
            return new RunningResultOperationResult(RunningStatus.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockedActivePlan = await db.RunningPlans.Where(plan => plan.Id == session.PlanId && plan.UserId == userId &&
                plan.IsActive)
            .ExecuteUpdateAsync(updates => updates.SetProperty(plan => plan.Version, plan => plan.Version), cancellationToken);
        if (lockedActivePlan == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningResultOperationResult(RunningStatus.Conflict);
        }

        var existingSessionResult = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.UserId == userId && candidate.SessionId == sessionId, cancellationToken);
        if (existingSessionResult is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RunningResultOperationResult(RunningStatus.Conflict, Map(existingSessionResult));
        }

        var now = UtcNow();
        var result = new RunningResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompletionId = input.CompletionId,
            SessionId = sessionId,
            Date = resultDate,
            DistanceKm = input.DistanceKm,
            DurationSeconds = input.DurationSeconds,
            AverageHeartRate = input.AverageHeartRate,
            Note = NormalizeNote(input.Note),
            Version = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.RunningResults.Add(result);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RunningResultOperationResult(RunningStatus.Saved, Map(result));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
            if (existing is not null && MatchesSessionCompletion(existing, sessionId, input))
            {
                return new RunningResultOperationResult(RunningStatus.Saved, Map(existing));
            }

            existingSessionResult = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.UserId == userId && candidate.SessionId == sessionId, cancellationToken);
            return new RunningResultOperationResult(RunningStatus.Conflict,
                existingSessionResult is null ? null : Map(existingSessionResult));
        }
    }

    public async Task<RunningResultData?> GetResultAsync(string userId, Guid resultId, CancellationToken cancellationToken)
    {
        var result = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == resultId && candidate.UserId == userId, cancellationToken);
        return result is null ? null : Map(result);
    }

    public async Task<RunningResultOperationResult> UpdateResultAsync(string userId, Guid resultId,
        UpdateRunningResultInput input, CancellationToken cancellationToken)
    {
        if (input is null || input.Version == Guid.Empty ||
            !IsValidResult(input.Date, input.DistanceKm, input.DurationSeconds, input.AverageHeartRate, input.Note,
                requireDistanceAndDuration: false))
        {
            return new RunningResultOperationResult(RunningStatus.Invalid);
        }

        var result = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == resultId && candidate.UserId == userId, cancellationToken);
        if (result is null)
        {
            return new RunningResultOperationResult(RunningStatus.NotFound);
        }

        if (result.Version != input.Version)
        {
            return new RunningResultOperationResult(RunningStatus.Conflict, Map(result));
        }

        if (result.SessionId is null && (input.DistanceKm is null || input.DurationSeconds is null))
        {
            return new RunningResultOperationResult(RunningStatus.Invalid);
        }

        if (result.SessionId is { } sessionId)
        {
            var session = await FindOwnedSessionAsync(userId, sessionId, cancellationToken);
            if (session is not null && input.Date < session.Date)
            {
                return new RunningResultOperationResult(RunningStatus.Invalid);
            }
        }

        var nextVersion = Guid.NewGuid();
        var now = UtcNow();
        var note = NormalizeNote(input.Note);
        var affected = await db.RunningResults.Where(candidate => candidate.Id == resultId && candidate.UserId == userId &&
                candidate.Version == input.Version)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Date, input.Date)
                .SetProperty(candidate => candidate.DistanceKm, input.DistanceKm)
                .SetProperty(candidate => candidate.DurationSeconds, input.DurationSeconds)
                .SetProperty(candidate => candidate.AverageHeartRate, input.AverageHeartRate)
                .SetProperty(candidate => candidate.Note, note)
                .SetProperty(candidate => candidate.Version, nextVersion)
                .SetProperty(candidate => candidate.UpdatedAtUtc, now), cancellationToken);
        if (affected == 0)
        {
            var current = await db.RunningResults.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == resultId && candidate.UserId == userId, cancellationToken);
            return new RunningResultOperationResult(current is null ? RunningStatus.NotFound : RunningStatus.Conflict,
                current is null ? null : Map(current));
        }

        return new RunningResultOperationResult(RunningStatus.Saved, new RunningResultData(resultId, result.SessionId,
            input.Date, input.DistanceKm, input.DurationSeconds, input.AverageHeartRate,
            Pace(input.DistanceKm, input.DurationSeconds), note, nextVersion, result.CreatedAtUtc, now));
    }

    private IQueryable<RunningPlan> QueryPlans() => db.RunningPlans.AsNoTracking()
        .Include(plan => plan.SelectedDays)
        .Include(plan => plan.Sessions);

    private async Task<RunningPlanData> MapPlanAsync(RunningPlan plan, CancellationToken cancellationToken)
    {
        var results = await ResultsBySessionAsync(plan.UserId, plan.Sessions.Select(session => session.Id), cancellationToken);
        return new RunningPlanData(plan.Id, plan.Version, plan.IsActive, plan.CreatedAtUtc, plan.ReplacedAtUtc,
            plan.Level, plan.ThirtyMinuteDistanceKm, plan.TargetDistanceKm, plan.TargetDate, plan.WeeklyFrequency,
            plan.SelectedDays.OrderBy(day => Array.IndexOf(WeekdayOrder, day.DayOfWeek)).Select(day => day.DayOfWeek).ToArray(),
            plan.Sessions.OrderBy(session => session.Date).ThenBy(session => session.Position)
                .Select(session => Map(session, results.GetValueOrDefault(session.Id))).ToArray());
    }

    private async Task<Dictionary<Guid, RunningResult>> ResultsBySessionAsync(string userId,
        IEnumerable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var ids = sessionIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return (await db.RunningResults.AsNoTracking().Where(result => result.UserId == userId &&
                result.SessionId != null && ids.Contains(result.SessionId.Value))
            .ToListAsync(cancellationToken)).ToDictionary(result => result.SessionId!.Value);
    }

    private Task<RunningSession?> FindOwnedSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken) =>
        (from session in db.RunningSessions.AsNoTracking()
         join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
         where session.Id == sessionId && plan.UserId == userId
         select session).SingleOrDefaultAsync(cancellationToken);

    private Task<RunningResult?> FindCompletionAsync(string userId, Guid completionId, CancellationToken cancellationToken) =>
        db.RunningResults.AsNoTracking().SingleOrDefaultAsync(result => result.UserId == userId &&
            result.CompletionId == completionId, cancellationToken);

    private Task<bool> ActivePlanExistsAsync(string userId, CancellationToken cancellationToken) =>
        db.RunningPlans.AsNoTracking().AnyAsync(plan => plan.UserId == userId && plan.IsActive, cancellationToken);

    private Task<bool> IsActivePlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
        db.RunningPlans.AsNoTracking().AnyAsync(plan => plan.Id == planId && plan.UserId == userId && plan.IsActive,
            cancellationToken);

    // The no-op update deliberately obtains the SQLite write lock shared by plan and session state changes.
    private Task<int> LockActivePlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
        db.RunningPlans.Where(plan => plan.Id == planId && plan.UserId == userId && plan.IsActive)
            .ExecuteUpdateAsync(updates => updates.SetProperty(plan => plan.Version, plan => plan.Version), cancellationToken);

    private async Task<RunningSessionResult> CurrentSessionResultAsync(string userId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        var current = await GetSessionAsync(userId, sessionId, cancellationToken);
        if (current is null)
        {
            return new RunningSessionResult(RunningStatus.NotFound);
        }

        return current.State == RunningSessionState.Completed
            ? new RunningSessionResult(RunningStatus.Conflict, current)
            : new RunningSessionResult(RunningStatus.Saved, current);
    }

    private bool IsValidPlanInput(string userId, RunningPlanInput? input, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(userId) || input is null ||
            !Enum.IsDefined(typeof(RunningLevel), input.Level) ||
            !RunningRules.IsValidThirtyMinuteDistance(input.ThirtyMinuteDistanceKm) ||
            !RunningRules.IsValidTargetDistance(input.TargetDistanceKm) || input.WeeklyFrequency is < 1 or > 7 ||
            input.TargetDate < today.AddDays(RunningRules.MinPlanLengthDays) ||
            input.TargetDate > today.AddDays(RunningRules.MaxPlanLengthDays) || input.SelectedDays is null ||
            input.SelectedDays.Count != input.WeeklyFrequency ||
            input.SelectedDays.Distinct().Count() != input.SelectedDays.Count ||
            input.SelectedDays.Any(day => day is < DayOfWeek.Sunday or > DayOfWeek.Saturday))
        {
            return false;
        }

        return true;
    }

    private bool IsValidResult(DateOnly date, decimal? distanceKm, int? durationSeconds, int? averageHeartRate,
        string? note, bool requireDistanceAndDuration)
    {
        var hasDistance = distanceKm is not null;
        var hasDuration = durationSeconds is not null;
        return date >= EarliestResultDate && date <= Today() && RunningRules.IsValidResultDistance(distanceKm) &&
            RunningRules.IsValidDuration(durationSeconds) && RunningRules.IsValidHeartRate(averageHeartRate) &&
            RunningRules.IsValidNote(note) && hasDistance == hasDuration &&
            (!requireDistanceAndDuration || hasDistance);
    }

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone).DateTime);

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

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static RunningSessionData Map(RunningSession session, RunningResult? result = null) => new(session.Id,
        session.PlanId, session.Date, session.Kind, session.PlannedDistanceKm, session.PaceMinSecondsPerKm,
        session.PaceMaxSecondsPerKm, session.Structure, session.StartedAtUtc,
        result is not null ? RunningSessionState.Completed : session.StartedAtUtc is null
            ? RunningSessionState.Planned
            : RunningSessionState.Started,
        result is null ? null : Map(result));

    private static RunningResultData Map(RunningResult result) => new(result.Id, result.SessionId, result.Date,
        result.DistanceKm, result.DurationSeconds, result.AverageHeartRate, Pace(result.DistanceKm, result.DurationSeconds),
        result.Note, result.Version, result.CreatedAtUtc, result.UpdatedAtUtc);

    private static int? Pace(decimal? distanceKm, int? durationSeconds) =>
        distanceKm is > 0m && durationSeconds is > 0
            ? (int?)decimal.Round(durationSeconds.Value / distanceKm.Value, 0, MidpointRounding.AwayFromZero)
            : null;

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static bool MatchesManualResult(RunningResult result, ManualRunningResultInput input) =>
        result.SessionId is null && result.Date == input.Date && result.DistanceKm == input.DistanceKm &&
        result.DurationSeconds == input.DurationSeconds && result.AverageHeartRate == input.AverageHeartRate &&
        result.Note == NormalizeNote(input.Note);

    private static bool MatchesSessionCompletion(RunningResult result, Guid sessionId, CompleteRunningSessionInput input) =>
        result.SessionId == sessionId && (input.Date is null || result.Date == input.Date) &&
        result.DistanceKm == input.DistanceKm && result.DurationSeconds == input.DurationSeconds &&
        result.AverageHeartRate == input.AverageHeartRate && result.Note == NormalizeNote(input.Note);
}
