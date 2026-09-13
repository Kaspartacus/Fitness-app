using FitnessApp.Contracts.Running;

namespace FitnessApp.Contracts.Calendar;

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

public sealed record CalendarActivityResponse(
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

public sealed record CalendarRangeResponse(IReadOnlyList<CalendarActivityResponse> Activities);
