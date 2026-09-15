using FitnessApp.Domain.Running;

namespace FitnessApp.Application.Calendar;

public enum CalendarActivityType
{
    Running,
    Strength
}

public enum CalendarActivityState
{
    Planned,
    Started,
    Completed
}

public sealed record CalendarActivityData(
    Guid Id,
    CalendarActivityType Type,
    CalendarActivityState State,
    DateOnly Date,
    string Title,
    RunningSessionKind? RunningKind,
    decimal? PlannedDistanceKm,
    decimal? ActualDistanceKm,
    int? ActualDurationSeconds,
    DateOnly? PlannedDate,
    Guid? RunningSessionId,
    Guid? RunningResultId,
    Guid? StrengthProgramId,
    Guid? StrengthWorkoutId,
    Guid? StrengthCompletionId);

public sealed record CalendarRangeData(IReadOnlyList<CalendarActivityData> Activities);

public enum CalendarMoveStatus
{
    Saved,
    NotFound,
    Invalid,
    Conflict
}

public sealed record MoveCalendarOccurrenceInput(DateOnly OriginalDate, DateOnly TargetDate);

public sealed record CalendarMoveResult(CalendarMoveStatus Status);

public interface ICalendarService
{
    Task<CalendarRangeData> GetRangeAsync(string userId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken);

    Task<CalendarMoveResult> MoveRunningOccurrenceAsync(string userId, Guid sessionId,
        MoveCalendarOccurrenceInput input, CancellationToken cancellationToken);

    Task<CalendarMoveResult> MoveStrengthOccurrenceAsync(string userId, Guid programId, Guid workoutId,
        MoveCalendarOccurrenceInput input, CancellationToken cancellationToken);
}
