using FitnessApp.Application.Calendar;
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

        var plannedRunning = await (from session in db.RunningSessions.AsNoTracking()
                                    join plan in db.RunningPlans.AsNoTracking() on session.PlanId equals plan.Id
                                    where plan.UserId == userId && plan.IsActive && session.Date >= rangeStart && session.Date <= rangeEnd &&
                                          !db.RunningResults.Any(result => result.UserId == userId && result.SessionId == session.Id)
                                    orderby session.Date, session.Position, session.Id
                                    select new PlannedRunningRow(
                                        session.Id,
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
                                                     session.Date >= rangeStart && session.Date <= rangeEnd
                                               select new ScheduledResultRow(
                                                   result.Id,
                                                   session.Id,
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

        var completionStart = CopenhagenMidnightUtc(rangeStart);
        var completionEnd = CopenhagenMidnightUtc(rangeEnd.AddDays(1));
        var completedStrength = await db.CompletedWorkouts.AsNoTracking()
            .Where(workout => workout.UserId == userId && workout.CompletedAt >= completionStart && workout.CompletedAt < completionEnd)
            .OrderBy(workout => workout.CompletedAt).ThenBy(workout => workout.Id)
            .Select(workout => new CompletedStrengthRow(
                workout.Id,
                workout.CompletedAt,
                workout.WorkoutName))
            .ToListAsync(cancellationToken);

        var resultRowsById = resultsInRange.ToDictionary(result => result.Id);
        foreach (var result in resultsForScheduledDates)
        {
            resultRowsById.TryAdd(result.ResultId, new RunningResultRow(result.ResultId, result.SessionId,
                result.ActualDate, result.ActualDistanceKm, result.ActualDurationSeconds));
        }

        var today = Today();
        var firstScheduledStrengthDate = rangeStart < today ? today : rangeStart;
        var scheduledStrengthDays = firstScheduledStrengthDate > rangeEnd
            ? 0
            : rangeEnd.DayNumber - firstScheduledStrengthDate.DayNumber + 1;
        var activities = new List<CalendarActivityData>(plannedRunning.Count + resultRowsById.Count +
            strengthSchedules.Count * scheduledStrengthDays +
            completedStrength.Count);

        activities.AddRange(plannedRunning.Select(session => new CalendarActivityData(
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
            null)));

        activities.AddRange(resultRowsById.Values.Select(result =>
        {
            scheduledResultsById.TryGetValue(result.Id, out var planned);
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
                planned?.Date,
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
                activities.Add(new CalendarActivityData(
                    schedule.WorkoutId,
                    CalendarActivityType.Strength,
                    CalendarActivityState.Planned,
                    date,
                    schedule.WorkoutName,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    schedule.ProgramId,
                    schedule.WorkoutId,
                    null));
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
            null,
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

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone).DateTime);

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

    private sealed record PlannedRunningRow(Guid Id, DateOnly Date, RunningSessionKind Kind, decimal PlannedDistanceKm,
        bool Started);

    private sealed record RunningResultRow(Guid Id, Guid? SessionId, DateOnly Date, decimal? DistanceKm,
        int? DurationSeconds);

    private sealed record ScheduledResultRow(Guid ResultId, Guid SessionId, DateOnly Date, RunningSessionKind Kind,
        decimal PlannedDistanceKm, DateOnly ActualDate, decimal? ActualDistanceKm, int? ActualDurationSeconds);

    private sealed record StrengthScheduleRow(Guid ProgramId, Guid WorkoutId, string WorkoutName, DayOfWeek DayOfWeek);

    private sealed record CompletedStrengthRow(Guid Id, DateTime CompletedAt, string WorkoutName);
}
