namespace FitnessApp.Contracts.Running;

public enum RunningLevel
{
    NewToRunning,
    Beginner,
    Recreational,
    Trained,
    Experienced
}

public enum RunningSessionKind
{
    Easy,
    Tempo,
    Intervals,
    LongRun
}

public enum RunningSessionState
{
    Planned,
    Started,
    Completed
}

public class RunningPlanRequest
{
    public RunningLevel Level { get; set; }
    public decimal ThirtyMinuteDistanceKm { get; set; }
    public decimal TargetDistanceKm { get; set; }
    public DateOnly TargetDate { get; set; }
    public int WeeklyFrequency { get; set; }
    public List<DayOfWeek>? SelectedDays { get; set; } = [];
}

public sealed class ReplaceRunningPlanRequest : RunningPlanRequest
{
    public Guid Version { get; set; }
    public bool ReplaceActivePlan { get; set; }
}

public sealed class UpdateRunningPlanScheduleRequest
{
    public Guid Version { get; set; }
    public List<DayOfWeek>? SelectedDays { get; set; } = [];
}

public sealed class ManualRunningResultRequest
{
    public Guid CompletionId { get; set; }
    public DateOnly Date { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? DurationSeconds { get; set; }
    public int? AverageHeartRate { get; set; }
    public string? Note { get; set; }
}

public sealed class CompleteRunningSessionRequest
{
    public Guid CompletionId { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? DurationSeconds { get; set; }
    public int? AverageHeartRate { get; set; }
    public string? Note { get; set; }
}

public sealed class UpdateRunningResultRequest
{
    public Guid Version { get; set; }
    public DateOnly Date { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? DurationSeconds { get; set; }
    public int? AverageHeartRate { get; set; }
    public string? Note { get; set; }
}

public sealed record RunningResultResponse(
    Guid Id,
    Guid? SessionId,
    DateOnly Date,
    decimal? DistanceKm,
    int? DurationSeconds,
    int? AverageHeartRate,
    int? PaceSecondsPerKm,
    string? Note,
    Guid Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record RunningSessionResponse(
    Guid Id,
    Guid PlanId,
    DateOnly Date,
    RunningSessionKind Kind,
    decimal PlannedDistanceKm,
    int PaceMinSecondsPerKm,
    int PaceMaxSecondsPerKm,
    string Structure,
    DateTime? StartedAtUtc,
    RunningSessionState State,
    RunningResultResponse? Result);

public sealed record RunningPlanResponse(
    Guid Id,
    Guid Version,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? ReplacedAtUtc,
    RunningLevel Level,
    decimal ThirtyMinuteDistanceKm,
    decimal TargetDistanceKm,
    DateOnly TargetDate,
    int WeeklyFrequency,
    IReadOnlyList<DayOfWeek> SelectedDays,
    IReadOnlyList<RunningSessionResponse> Sessions);

public sealed record RunningOverviewResponse(
    RunningPlanResponse? ActivePlan,
    IReadOnlyList<RunningSessionResponse> UpcomingSessions,
    IReadOnlyList<RunningResultResponse> Results);
