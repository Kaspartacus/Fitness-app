using FitnessApp.Application.Calendar;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Running;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Calendar;

public sealed class CalendarService(FitnessDbContext db, TimeProvider timeProvider) : ICalendarService
{
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();

    public async Task<CalendarRangeData> GetRangeAsync(string userId, DateOnly rangeStart, DateOnly rangeEnd,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || rangeStart > rangeEnd ||
            rangeEnd.DayNumber - rangeStart.DayNumber + 1 > 93)
        {
            return new CalendarRangeData([]);
        }

        // The range is deliberately bounded before querying both source and target dates. A move can cross the
        // current week boundary, so either date is enough to make the source occurrence relevant to this response.
        var occurrenceMoves = await db.CalendarOccurrenceMoves.AsNoTracking()
            .Where(move => move.UserId == userId &&
                           ((move.OriginalDate >= rangeStart && move.OriginalDate <= rangeEnd) ||
                            (move.TargetDate >= rangeStart && move.TargetDate <= rangeEnd)))
            .ToListAsync(cancellationToken);
        var movedRunningSessionIdsInRange = occurrenceMoves
            .Where(move => move.Kind == CalendarOccurrenceKind.Running &&
                           move.TargetDate >= rangeStart && move.TargetDate <= rangeEnd)
            .Select(move => move.SourceId)
            .Distinct()
            .ToArray();
        var runningMovesBySessionId = occurrenceMoves
            .Where(move => move.Kind == CalendarOccurrenceKind.Running)
            .GroupBy(move => move.SourceId)
            .ToDictionary(group => group.Key, group => group.First());
        var strengthMovesByOccurrence = occurrenceMoves
            .Where(move => move.Kind == CalendarOccurrenceKind.Strength)
            .GroupBy(move => new StrengthOccurrenceKey(move.ScopeId, move.SourceId, move.OriginalDate))
            .ToDictionary(group => group.Key, group => group.First());

        var plannedRunning = await (from session in db.RunningSessions.AsNoTracking()
                                    join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
                                    where plan.UserId == userId && plan.IsActive &&
                                          !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == session.Id) &&
                                          ((session.Date >= rangeStart && session.Date <= rangeEnd) ||
                                           movedRunningSessionIdsInRange.Contains(session.Id))
                                    orderby session.Date, session.Position, session.Id
                                    select new PlannedRunningRow(
                                        session.Id,
                                        session.PlanId,
                                        session.Date,
                                        session.Kind,
                                        session.PlannedDistanceKm,
                                        session.StartedAtUtc != null))
            .ToListAsync(cancellationToken);

        var resultsInRange = await db.RunningResults.AsNoTracking()
            .Where(result => result.UserId == userId && result.Date >= rangeStart && result.Date <= rangeEnd)
            .OrderBy(result => result.Date).ThenBy(result => result.UpdatedAtUtc).ThenBy(result => result.Id)
            .Select(result => new RunningResultRow(
                result.Id,
                result.SessionId,
                result.Date,
                result.DistanceKm,
                result.DurationSeconds))
            .ToListAsync(cancellationToken);

        var resultsForScheduledDates = await (from session in db.RunningSessions.AsNoTracking()
                                               join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
                                               join result in db.RunningResults.AsNoTracking() on (Guid?)session.Id equals result.SessionId
                                               where plan.UserId == userId && result.UserId == userId &&
                                                     ((session.Date >= rangeStart && session.Date <= rangeEnd) ||
                                                      movedRunningSessionIdsInRange.Contains(session.Id))
                                               select new ScheduledResultRow(
                                                   result.Id,
                                                   session.Id,
                                                   plan.Id,
                                                   session.Date,
                                                   session.Kind,
                                                   session.PlannedDistanceKm,
                                                   result.Date,
                                                   result.DistanceKm,
                                                   result.DurationSeconds))
            .ToListAsync(cancellationToken);

        var scheduledResultsById = resultsForScheduledDates
            .GroupBy(result => result.ResultId)
            .ToDictionary(group => group.Key, group => group.First());
        var resultSessionIds = resultsInRange
            .Where(result => result.SessionId is not null && !scheduledResultsById.ContainsKey(result.Id))
            .Select(result => result.SessionId!.Value)
            .Distinct()
            .ToArray();
        if (resultSessionIds.Length > 0)
        {
            var matchingSessions = await (from session in db.RunningSessions.AsNoTracking()
                                          join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
                                          where plan.UserId == userId && resultSessionIds.Contains(session.Id)
                                          select new ScheduledResultRow(
                                              Guid.Empty,
                                              session.Id,
                                              plan.Id,
                                              session.Date,
                                              session.Kind,
                                              session.PlannedDistanceKm,
                                              default,
                                              null,
                                              null))
                .ToListAsync(cancellationToken);
            var sessionsById = matchingSessions.ToDictionary(session => session.SessionId);
            foreach (var result in resultsInRange.Where(result => result.SessionId is not null))
            {
                if (!scheduledResultsById.ContainsKey(result.Id) &&
                    sessionsById.TryGetValue(result.SessionId!.Value, out var session))
                {
                    scheduledResultsById[result.Id] = session with { ResultId = result.Id };
                }
            }
        }

        var strengthSchedules = await (from schedule in db.ProgramScheduleEntries.AsNoTracking()
                                       join program in db.StrengthPrograms.AsNoTracking() on schedule.ProgramId equals program.Id
                                       join workout in db.ProgramWorkouts.AsNoTracking() on schedule.WorkoutId equals (Guid?)workout.Id
                                       where program.UserId == userId && schedule.WorkoutId != null &&
                                             workout.ProgramId == program.Id
                                       select new StrengthScheduleRow(
                                           program.Id,
                                           workout.Id,
                                           workout.Name,
                                           schedule.DayOfWeek))
            .ToListAsync(cancellationToken);

        var today = Today();
        var firstScheduledStrengthDate = rangeStart < today ? today : rangeStart;
        var completionStart = CopenhagenMidnightUtc(rangeStart);
        var completionEnd = CopenhagenMidnightUtc(rangeEnd.AddDays(1));
        var completedStrength = await db.CompletedWorkouts.AsNoTracking()
            .Where(workout => workout.UserId == userId && workout.CompletedAt >= completionStart && workout.CompletedAt < completionEnd)
            .OrderBy(workout => workout.CompletedAt).ThenBy(workout => workout.Id)
            .Select(workout => new CompletedStrengthRow(
                workout.Id,
                workout.CompletedAt,
                workout.WorkoutName,
                workout.ProgramId,
                workout.WorkoutId,
                workout.ScheduledOccurrenceDate))
            .ToListAsync(cancellationToken);
        var completedStrengthOccurrenceKeys = completedStrength
            .Where(workout => workout.ScheduledOccurrenceDate is not null)
            .Select(workout => new StrengthOccurrenceKey(workout.ProgramId, workout.WorkoutId,
                workout.ScheduledOccurrenceDate!.Value))
            .ToHashSet();
        if (firstScheduledStrengthDate <= rangeEnd)
        {
            completedStrengthOccurrenceKeys.UnionWith(await db.CompletedWorkouts.AsNoTracking()
                .Where(workout => workout.UserId == userId && workout.ScheduledOccurrenceDate != null &&
                                  workout.ScheduledOccurrenceDate >= firstScheduledStrengthDate &&
                                  workout.ScheduledOccurrenceDate <= rangeEnd)
                .Select(workout => new StrengthOccurrenceKey(workout.ProgramId, workout.WorkoutId,
                    workout.ScheduledOccurrenceDate!.Value))
                .ToListAsync(cancellationToken));
        }
        completedStrengthOccurrenceKeys.UnionWith(await (from completed in db.CompletedWorkouts.AsNoTracking()
                                                           where completed.UserId == userId &&
                                                                 completed.ScheduledOccurrenceDate != null
                                                           join move in db.CalendarOccurrenceMoves.AsNoTracking()
                                                               on new
                                                               {
                                                                   completed.UserId,
                                                                   ScopeId = completed.ProgramId,
                                                                   SourceId = completed.WorkoutId,
                                                                   OriginalDate = completed.ScheduledOccurrenceDate!.Value
                                                               }
                                                               equals new { move.UserId, move.ScopeId, move.SourceId, move.OriginalDate }
                                                           where move.Kind == CalendarOccurrenceKind.Strength &&
                                                                 move.TargetDate >= rangeStart && move.TargetDate <= rangeEnd
                                                           select new StrengthOccurrenceKey(completed.ProgramId,
                                                               completed.WorkoutId,
                                                               completed.ScheduledOccurrenceDate!.Value))
            .ToListAsync(cancellationToken));
        var completedStrengthIds = completedStrength.Where(workout => workout.ScheduledOccurrenceDate is not null)
            .Select(workout => workout.Id)
            .ToArray();
        var completedStrengthMoveDates = completedStrengthIds.Length == 0
            ? new Dictionary<Guid, DateOnly>()
            : (await (from completed in db.CompletedWorkouts.AsNoTracking()
                      where completedStrengthIds.Contains(completed.Id) && completed.ScheduledOccurrenceDate != null
                      join move in db.CalendarOccurrenceMoves.AsNoTracking()
                          on new
                          {
                              completed.UserId,
                              ScopeId = completed.ProgramId,
                              SourceId = completed.WorkoutId,
                              OriginalDate = completed.ScheduledOccurrenceDate!.Value
                          }
                          equals new { move.UserId, move.ScopeId, move.SourceId, move.OriginalDate }
                      where move.Kind == CalendarOccurrenceKind.Strength
                      select new CompletedStrengthMoveRow(completed.Id, move.TargetDate))
                .ToListAsync(cancellationToken))
                .GroupBy(item => item.CompletedWorkoutId)
                .ToDictionary(group => group.Key, group => group.First().TargetDate);

        var resultRowsById = resultsInRange.ToDictionary(result => result.Id);
        foreach (var result in resultsForScheduledDates)
        {
            resultRowsById.TryAdd(result.ResultId, new RunningResultRow(result.ResultId, result.SessionId,
                result.ActualDate, result.ActualDistanceKm, result.ActualDurationSeconds));
        }
        var scheduledResultSessionIds = scheduledResultsById.Values.Select(result => result.SessionId).Distinct().ToArray();
        var resultMovesBySessionId = scheduledResultSessionIds.Length == 0
            ? new Dictionary<Guid, CalendarOccurrenceMove>()
            : (await db.CalendarOccurrenceMoves.AsNoTracking()
                    .Where(move => move.UserId == userId && move.Kind == CalendarOccurrenceKind.Running &&
                                   scheduledResultSessionIds.Contains(move.SourceId))
                    .ToListAsync(cancellationToken))
                .GroupBy(move => move.SourceId)
                .ToDictionary(group => group.Key, group => group.First());

        var scheduledStrengthDays = firstScheduledStrengthDate > rangeEnd
            ? 0
            : rangeEnd.DayNumber - firstScheduledStrengthDate.DayNumber + 1;
        var activities = new List<CalendarActivityData>(plannedRunning.Count + resultRowsById.Count +
            strengthSchedules.Count * scheduledStrengthDays + occurrenceMoves.Count + completedStrength.Count);

        foreach (var session in plannedRunning)
        {
            var hasMove = runningMovesBySessionId.TryGetValue(session.Id, out var move) &&
                move.ScopeId == session.PlanId && move.OriginalDate == session.Date;
            if (hasMove)
            {
                if (move!.TargetDate < rangeStart || move.TargetDate > rangeEnd)
                {
                    continue;
                }

                activities.Add(new CalendarActivityData(
                    session.Id,
                    CalendarActivityType.Running,
                    session.Started ? CalendarActivityState.Started : CalendarActivityState.Planned,
                    move.TargetDate,
                    "",
                    session.Kind,
                    session.PlannedDistanceKm,
                    null,
                    null,
                    session.Date,
                    session.Id,
                    null,
                    null,
                    null,
                    null));
                continue;
            }

            if (session.Date < rangeStart || session.Date > rangeEnd)
            {
                continue;
            }

            activities.Add(new CalendarActivityData(
                session.Id,
                CalendarActivityType.Running,
                session.Started ? CalendarActivityState.Started : CalendarActivityState.Planned,
                session.Date,
                "",
                session.Kind,
                session.PlannedDistanceKm,
                null,
                null,
                null,
                session.Id,
                null,
                null,
                null,
                null));
        }

        activities.AddRange(resultRowsById.Values.Select(result =>
        {
            scheduledResultsById.TryGetValue(result.Id, out var planned);
            var plannedDate = planned is not null && resultMovesBySessionId.TryGetValue(planned.SessionId, out var move) &&
                              move.ScopeId == planned.PlanId && move.OriginalDate == planned.Date
                ? move.TargetDate
                : planned?.Date;
            return new CalendarActivityData(
                result.Id,
                CalendarActivityType.Running,
                CalendarActivityState.Completed,
                result.Date,
                "",
                planned?.Kind,
                planned?.PlannedDistanceKm,
                result.DistanceKm,
                result.DurationSeconds,
                plannedDate,
                planned?.SessionId,
                result.Id,
                null,
                null,
                null);
        }));

        for (var date = firstScheduledStrengthDate; date <= rangeEnd; date = date.AddDays(1))
        {
            foreach (var schedule in strengthSchedules.Where(schedule => schedule.DayOfWeek == date.DayOfWeek))
            {
                var occurrence = new StrengthOccurrenceKey(schedule.ProgramId, schedule.WorkoutId, date);
                if (strengthMovesByOccurrence.ContainsKey(occurrence) || completedStrengthOccurrenceKeys.Contains(occurrence))
                {
                    continue;
                }

                activities.Add(PlannedStrengthActivity(schedule, date, null));
            }
        }

        foreach (var move in occurrenceMoves.Where(move => move.Kind == CalendarOccurrenceKind.Strength &&
                     move.TargetDate >= rangeStart && move.TargetDate <= rangeEnd))
        {
            var schedule = strengthSchedules.FirstOrDefault(candidate =>
                candidate.ProgramId == move.ScopeId && candidate.WorkoutId == move.SourceId &&
                candidate.DayOfWeek == move.OriginalDate.DayOfWeek);
            var occurrence = new StrengthOccurrenceKey(move.ScopeId, move.SourceId, move.OriginalDate);
            if (schedule is not null && !completedStrengthOccurrenceKeys.Contains(occurrence))
            {
                activities.Add(PlannedStrengthActivity(schedule, move.TargetDate, move.OriginalDate));
            }
        }

        activities.AddRange(completedStrength.Select(workout => new CalendarActivityData(
            workout.Id,
            CalendarActivityType.Strength,
            CalendarActivityState.Completed,
            CopenhagenDate(workout.CompletedAt),
            workout.WorkoutName,
            null,
            null,
            null,
            null,
            workout.ScheduledOccurrenceDate is { } originalDate
                ? completedStrengthMoveDates.GetValueOrDefault(workout.Id, originalDate)
                : null,
            null,
            null,
            null,
            null,
            workout.Id)));

        return new CalendarRangeData(activities
            .OrderBy(activity => activity.Date)
            .ThenBy(activity => activity.State == CalendarActivityState.Completed ? 0 : 1)
            .ThenBy(activity => activity.Type)
            .ThenBy(activity => activity.Title)
            .ThenBy(activity => activity.Id)
            .ToArray());
    }

    public async Task<CalendarMoveResult> MoveRunningOccurrenceAsync(string userId, Guid sessionId,
        MoveCalendarOccurrenceInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || sessionId == Guid.Empty || input is null ||
            input.OriginalDate == default || input.TargetDate == default)
        {
            return new CalendarMoveResult(CalendarMoveStatus.Invalid);
        }

        var source = await FindRunningMoveSourceAsync(userId, sessionId, cancellationToken);
        if (source is null)
        {
            return new CalendarMoveResult(CalendarMoveStatus.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await LockActiveRunningPlanAsync(userId, source.PlanId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CalendarMoveResult(CalendarMoveStatus.Conflict);
        }

        source = await FindRunningMoveSourceAsync(userId, sessionId, cancellationToken);
        if (source is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CalendarMoveResult(CalendarMoveStatus.NotFound);
        }

        var existing = await FindMoveAsync(userId, CalendarOccurrenceKind.Running, source.PlanId, source.SessionId,
            input.OriginalDate, cancellationToken);
        var today = Today();
        if (source.Date != input.OriginalDate || source.Started || source.HasResult || input.TargetDate < today ||
            input.TargetDate > source.PlanTargetDate || (existing is null && input.OriginalDate < today) ||
            (existing is not null && existing.TargetDate < today))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CalendarMoveResult(CalendarMoveStatus.Invalid);
        }

        var moveResult = await SaveMoveAsync(existing, userId, CalendarOccurrenceKind.Running, source.PlanId, source.SessionId,
            input.OriginalDate, input.TargetDate, cancellationToken);
        if (moveResult.Status == CalendarMoveStatus.Saved)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return moveResult;
    }

    public async Task<CalendarMoveResult> MoveStrengthOccurrenceAsync(string userId, Guid programId, Guid workoutId,
        MoveCalendarOccurrenceInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || programId == Guid.Empty || workoutId == Guid.Empty || input is null ||
            input.OriginalDate == default || input.TargetDate == default)
        {
            return new CalendarMoveResult(CalendarMoveStatus.Invalid);
        }

        var sourceExists = await (from program in db.StrengthPrograms.AsNoTracking()
                                  join schedule in db.ProgramScheduleEntries.AsNoTracking() on program.Id equals schedule.ProgramId
                                  join workout in db.ProgramWorkouts.AsNoTracking() on schedule.WorkoutId equals (Guid?)workout.Id
                                  where program.Id == programId && program.UserId == userId && workout.Id == workoutId &&
                                        workout.ProgramId == program.Id && schedule.DayOfWeek == input.OriginalDate.DayOfWeek
                                  select schedule.Id)
            .AnyAsync(cancellationToken);
        if (!sourceExists)
        {
            return new CalendarMoveResult(CalendarMoveStatus.NotFound);
        }

        if (await db.CompletedWorkouts.AsNoTracking().AnyAsync(workout => workout.UserId == userId &&
                workout.ProgramId == programId && workout.WorkoutId == workoutId &&
                workout.ScheduledOccurrenceDate == input.OriginalDate, cancellationToken))
        {
            return new CalendarMoveResult(CalendarMoveStatus.Invalid);
        }

        var existing = await FindMoveAsync(userId, CalendarOccurrenceKind.Strength, programId, workoutId,
            input.OriginalDate, cancellationToken);
        var today = Today();
        if (input.TargetDate < today || (existing is null && input.OriginalDate < today) ||
            (existing is not null && existing.TargetDate < today))
        {
            return new CalendarMoveResult(CalendarMoveStatus.Invalid);
        }

        return await SaveMoveAsync(existing, userId, CalendarOccurrenceKind.Strength, programId, workoutId,
            input.OriginalDate, input.TargetDate, cancellationToken);
    }

    private Task<CalendarOccurrenceMove?> FindMoveAsync(string userId, CalendarOccurrenceKind kind, Guid scopeId,
        Guid sourceId, DateOnly originalDate, CancellationToken cancellationToken) =>
        db.CalendarOccurrenceMoves.SingleOrDefaultAsync(move => move.UserId == userId && move.Kind == kind &&
            move.ScopeId == scopeId && move.SourceId == sourceId && move.OriginalDate == originalDate, cancellationToken);

    private Task<RunningMoveSource?> FindRunningMoveSourceAsync(string userId, Guid sessionId,
        CancellationToken cancellationToken) =>
        (from session in db.RunningSessions.AsNoTracking()
         join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
         where session.Id == sessionId && plan.UserId == userId && plan.IsActive
         select new RunningMoveSource(
             session.Id,
             session.PlanId,
             session.Date,
             plan.TargetDate,
             session.StartedAtUtc != null,
             db.RunningResults.Any(result => result.UserId == userId && result.SessionId == session.Id)))
        .SingleOrDefaultAsync(cancellationToken);

    // The no-op update shares the SQLite write lock used when a running schedule is regenerated.
    private Task<int> LockActiveRunningPlanAsync(string userId, Guid planId, CancellationToken cancellationToken) =>
        db.RunningPlans.Where(plan => plan.Id == planId && plan.UserId == userId && plan.IsActive)
            .ExecuteUpdateAsync(updates => updates.SetProperty(plan => plan.Version, plan => plan.Version), cancellationToken);

    private async Task<CalendarMoveResult> SaveMoveAsync(CalendarOccurrenceMove? existing, string userId,
        CalendarOccurrenceKind kind, Guid scopeId, Guid sourceId, DateOnly originalDate, DateOnly targetDate,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (targetDate == originalDate)
        {
            if (existing is null)
            {
                return new CalendarMoveResult(CalendarMoveStatus.Invalid);
            }

            db.CalendarOccurrenceMoves.Remove(existing);
        }
        else if (existing is null)
        {
            db.CalendarOccurrenceMoves.Add(new CalendarOccurrenceMove
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = kind,
                ScopeId = scopeId,
                SourceId = sourceId,
                OriginalDate = originalDate,
                TargetDate = targetDate,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            existing.TargetDate = targetDate;
            existing.UpdatedAtUtc = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new CalendarMoveResult(CalendarMoveStatus.Saved);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new CalendarMoveResult(CalendarMoveStatus.Conflict);
        }
    }

    private static CalendarActivityData PlannedStrengthActivity(StrengthScheduleRow schedule, DateOnly date,
        DateOnly? originalDate) => new(
        schedule.WorkoutId,
        CalendarActivityType.Strength,
        CalendarActivityState.Planned,
        date,
        schedule.WorkoutName,
        null,
        null,
        null,
        null,
        originalDate,
        null,
        null,
        schedule.ProgramId,
        schedule.WorkoutId,
        null);

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone).DateTime);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateOnly CopenhagenDate(DateTime utc) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), CopenhagenTimeZone));

    private static DateTime CopenhagenMidnightUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), CopenhagenTimeZone);

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

    private sealed record PlannedRunningRow(Guid Id, Guid PlanId, DateOnly Date, RunningSessionKind Kind,
        decimal PlannedDistanceKm, bool Started);

    private sealed record RunningResultRow(Guid Id, Guid? SessionId, DateOnly Date, decimal? DistanceKm,
        int? DurationSeconds);

    private sealed record ScheduledResultRow(Guid ResultId, Guid SessionId, Guid PlanId, DateOnly Date, RunningSessionKind Kind,
        decimal PlannedDistanceKm, DateOnly ActualDate, decimal? ActualDistanceKm, int? ActualDurationSeconds);

    private sealed record StrengthScheduleRow(Guid ProgramId, Guid WorkoutId, string WorkoutName, DayOfWeek DayOfWeek);

    private sealed record CompletedStrengthRow(Guid Id, DateTime CompletedAt, string WorkoutName, Guid ProgramId,
        Guid WorkoutId, DateOnly? ScheduledOccurrenceDate);

    private sealed record CompletedStrengthMoveRow(Guid CompletedWorkoutId, DateOnly TargetDate);

    private sealed record RunningMoveSource(Guid SessionId, Guid PlanId, DateOnly Date, DateOnly PlanTargetDate,
        bool Started, bool HasResult);

    private readonly record struct StrengthOccurrenceKey(Guid ProgramId, Guid WorkoutId, DateOnly OriginalDate);
}
