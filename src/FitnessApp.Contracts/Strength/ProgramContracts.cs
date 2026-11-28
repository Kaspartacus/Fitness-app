namespace FitnessApp.Contracts.Strength;

public sealed class SaveProgramRequest
{
    public string? Name { get; set; } = "";
    public Guid? Version { get; set; }
    public List<WorkoutRequest>? Workouts { get; set; } = [];
}

public sealed class WorkoutRequest
{
    public Guid? Id { get; set; }
    public string? Name { get; set; } = "";
    public List<ExerciseRequest>? Exercises { get; set; } = [];
}

public sealed class ExerciseRequest
{
    public Guid? Id { get; set; }
    public string? Name { get; set; } = "";
    public decimal Weight { get; set; }
    public int Sets { get; set; } = 3;
    public int Repetitions { get; set; } = 10;
    public string? Note { get; set; }
    public string? CatalogExerciseId { get; set; }
    public int? DurationSeconds { get; set; }
}

public sealed record CatalogExerciseResponse(string Id, string Name, string Category,
    IReadOnlyList<string> SearchAliases, string ImageAlt, bool IsTimed);
public sealed record ExerciseResponse(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note)
{
    public string? CatalogExerciseId { get; init; }
    public int? DurationSeconds { get; init; }
}
public sealed record WorkoutResponse(Guid Id, string Name, IReadOnlyList<ExerciseResponse> Exercises);
public sealed record ProgramResponse(Guid Id, string Name, Guid Version, IReadOnlyList<WorkoutResponse> Workouts);
public sealed record ScheduleEntryRequest(DayOfWeek DayOfWeek, Guid? WorkoutId);
public sealed record ScheduleResponse(Guid Version, IReadOnlyList<ScheduleEntryRequest> Entries);
public sealed record PlannedWorkoutResponse(Guid ProgramId, string ProgramName, Guid WorkoutId, string WorkoutName,
    IReadOnlyList<ActiveExerciseResponse> Exercises);
public sealed record ActiveExerciseResponse(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note,
    decimal? PreviousWeight, int? PreviousSets, int? PreviousRepetitions)
{
    public string? CatalogExerciseId { get; init; }
    public int? DurationSeconds { get; init; }
    public int? PreviousDurationSeconds { get; init; }
}
public sealed record StrengthOverviewResponse(IReadOnlyList<ProgramResponse> Programs, PlannedWorkoutResponse? Today);

public sealed class CompleteWorkoutRequest
{
    public Guid CompletionId { get; set; }
    public DateOnly? ScheduledOccurrenceDate { get; set; }
    public List<CompletedExerciseRequest>? Exercises { get; set; } = [];
}

public sealed class CompletedExerciseRequest
{
    public Guid? ProgramExerciseId { get; set; }
    public string? Name { get; set; } = "";
    public decimal Weight { get; set; }
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public bool IsCompleted { get; set; }
    public int? DurationSeconds { get; set; }
}

public sealed record CompletedWorkoutExerciseResponse(string Name, decimal Weight, int Sets, int Repetitions,
    bool IsCompleted)
{
    public string? CatalogExerciseId { get; init; }
    public int? DurationSeconds { get; init; }
}
public sealed record CompletedWorkoutResponse(Guid Id, DateOnly Date, string WorkoutName,
    IReadOnlyList<CompletedWorkoutExerciseResponse> Exercises);

public sealed record StrengthProgressionDayResponse(DateOnly Date, decimal? MaxWeight, int? MaxRepetitions,
    decimal? VolumeKg, int? MaxDurationSeconds);
public sealed record StrengthExerciseProgressionResponse(string Key, string Name, bool IsTimed,
    IReadOnlyList<StrengthProgressionDayResponse> Days);

public sealed record StrengthMilestonesResponse(decimal TotalVolumeKg, int VolumeGoalKg, DateOnly? VolumeEarnedOn,
    bool HasRecordBaseline, DateOnly? RecordEarnedOn);
