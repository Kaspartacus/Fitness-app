namespace FitnessApp.Domain.Running;

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

public sealed class RunningPlan
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid Version { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReplacedAtUtc { get; set; }
    public RunningLevel Level { get; set; }
    public decimal ThirtyMinuteDistanceKm { get; set; }
    public decimal TargetDistanceKm { get; set; }
    public DateOnly TargetDate { get; set; }
    public int WeeklyFrequency { get; set; }
    public List<RunningPlanDay> SelectedDays { get; set; } = [];
    public List<RunningSession> Sessions { get; set; } = [];
}

public sealed class RunningPlanDay
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
}

public sealed class RunningSession
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public DateOnly Date { get; set; }
    public int Position { get; set; }
    public RunningSessionKind Kind { get; set; }
    public decimal PlannedDistanceKm { get; set; }
    public int PaceMinSecondsPerKm { get; set; }
    public int PaceMaxSecondsPerKm { get; set; }
    public string Structure { get; set; } = "";
    public DateTime? StartedAtUtc { get; set; }
}

public sealed class RunningResult
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid CompletionId { get; set; }
    public Guid? SessionId { get; set; }
    public DateOnly Date { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? DurationSeconds { get; set; }
    public int? AverageHeartRate { get; set; }
    public string? Note { get; set; }
    public Guid Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class RunningRules
{
    public const decimal MinThirtyMinuteDistanceKm = 0.5m;
    public const decimal MaxThirtyMinuteDistanceKm = 15m;
    public const decimal MinTargetDistanceKm = 1m;
    public const decimal MaxTargetDistanceKm = 100m;
    public const decimal MinResultDistanceKm = 0.1m;
    public const decimal MaxResultDistanceKm = 200m;
    public const int MinDurationSeconds = 1;
    public const int MaxDurationSeconds = 24 * 60 * 60;
    public const int MinHeartRate = 20;
    public const int MaxHeartRate = 260;
    public const int MinPaceSecondsPerKm = 150;
    public const int MaxPaceSecondsPerKm = 3_600;
    public const int MaxNoteLength = 500;
    public const int MaxStructureLength = 500;
    public const int MaxPlanLengthDays = 365;
    public const int MinPlanLengthDays = 7;
    public const int MaxCalendarRangeDays = 93;

    public static bool IsValidThirtyMinuteDistance(decimal distance) =>
        distance is >= MinThirtyMinuteDistanceKm and <= MaxThirtyMinuteDistanceKm;

    public static bool IsValidTargetDistance(decimal distance) =>
        distance is >= MinTargetDistanceKm and <= MaxTargetDistanceKm;

    public static bool IsValidResultDistance(decimal? distance) =>
        distance is null or >= MinResultDistanceKm and <= MaxResultDistanceKm;

    public static bool IsValidDuration(int? durationSeconds) =>
        durationSeconds is null or >= MinDurationSeconds and <= MaxDurationSeconds;

    public static bool IsValidHeartRate(int? heartRate) =>
        heartRate is null or >= MinHeartRate and <= MaxHeartRate;

    public static bool IsValidNote(string? note) => note?.Trim().Length is not > MaxNoteLength;

}
