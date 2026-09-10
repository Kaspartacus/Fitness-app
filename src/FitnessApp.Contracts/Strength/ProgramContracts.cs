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
}

public sealed record ExerciseResponse(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note);
public sealed record WorkoutResponse(Guid Id, string Name, IReadOnlyList<ExerciseResponse> Exercises);
public sealed record ProgramResponse(Guid Id, string Name, Guid Version, IReadOnlyList<WorkoutResponse> Workouts);
public sealed record ScheduleEntryRequest(DayOfWeek DayOfWeek, Guid? WorkoutId);
public sealed record ScheduleResponse(Guid Version, IReadOnlyList<ScheduleEntryRequest> Entries);
public sealed record PlannedWorkoutResponse(Guid ProgramId, string ProgramName, Guid WorkoutId, string WorkoutName,
    IReadOnlyList<ActiveExerciseResponse> Exercises);
public sealed record ActiveExerciseResponse(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note,
    decimal? PreviousWeight, int? PreviousSets, int? PreviousRepetitions);
public sealed record StrengthOverviewResponse(IReadOnlyList<ProgramResponse> Programs, PlannedWorkoutResponse? Today);

public sealed class CompleteWorkoutRequest
{
    public Guid CompletionId { get; set; }
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
}
