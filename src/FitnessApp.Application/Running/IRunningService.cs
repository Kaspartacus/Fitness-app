using FitnessApp.Domain.Running;

namespace FitnessApp.Application.Running;

public sealed record RunningPlanInput(
    RunningLevel Level,
    decimal ThirtyMinuteDistanceKm,
    decimal TargetDistanceKm,
    DateOnly TargetDate,
    int WeeklyFrequency,
    IReadOnlyList<DayOfWeek>? SelectedDays);

public sealed record RunningPlanReplacementInput(
    Guid Version,
    bool ReplaceActivePlan,
    RunningPlanInput Plan);

public sealed record ManualRunningResultInput(
    Guid CompletionId,
    DateOnly Date,
    decimal? DistanceKm,
    int? DurationSeconds,
    int? AverageHeartRate,
    string? Note);

public sealed record CompleteRunningSessionInput(
    Guid CompletionId,
    DateOnly? Date,
    decimal? DistanceKm,
    int? DurationSeconds,
    int? AverageHeartRate,
    string? Note);

public sealed record UpdateRunningResultInput(
    Guid Version,
    DateOnly Date,
    decimal? DistanceKm,
    int? DurationSeconds,
    int? AverageHeartRate,
    string? Note);

public enum RunningSessionState
{
    Planned,
    Started,
    Completed
}

public enum RunningStatus
{
    Saved,
    NotFound,
    Invalid,
    Infeasible,
    Conflict,
    ReplacementRequired
}

public sealed record RunningResultData(
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

public sealed record RunningSessionData(
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
    RunningResultData? Result);

public sealed record RunningPlanData(
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
    IReadOnlyList<RunningSessionData> Sessions);

public sealed record RunningOverviewData(
    RunningPlanData? ActivePlan,
    IReadOnlyList<RunningSessionData> UpcomingSessions,
    IReadOnlyList<RunningResultData> Results);

public sealed record RunningPlanResult(RunningStatus Status, RunningPlanData? Plan = null);
public sealed record RunningResultOperationResult(RunningStatus Status, RunningResultData? Result = null);
public sealed record RunningSessionResult(RunningStatus Status, RunningSessionData? Session = null);
public sealed record RunningSessionListResult(RunningStatus Status, IReadOnlyList<RunningSessionData> Sessions);

public interface IRunningService
{
    Task<RunningOverviewData> GetOverviewAsync(string userId, CancellationToken cancellationToken);
    Task<RunningPlanData?> GetPlanAsync(string userId, Guid planId, CancellationToken cancellationToken);
    Task<RunningPlanResult> CreatePlanAsync(string userId, RunningPlanInput input, CancellationToken cancellationToken);
    Task<RunningPlanResult> ReplacePlanAsync(string userId, Guid activePlanId, RunningPlanReplacementInput input,
        CancellationToken cancellationToken);
    Task<RunningSessionListResult> ListSessionsAsync(string userId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken);
    Task<RunningSessionData?> GetSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken);
    Task<RunningSessionResult> StartSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken);
    Task<RunningSessionResult> CancelSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken);
    Task<RunningResultOperationResult> CreateManualResultAsync(string userId, ManualRunningResultInput input,
        CancellationToken cancellationToken);
    Task<RunningResultOperationResult> CompleteSessionAsync(string userId, Guid sessionId,
        CompleteRunningSessionInput input, CancellationToken cancellationToken);
    Task<RunningResultData?> GetResultAsync(string userId, Guid resultId, CancellationToken cancellationToken);
    Task<RunningResultOperationResult> UpdateResultAsync(string userId, Guid resultId, UpdateRunningResultInput input,
        CancellationToken cancellationToken);
}
