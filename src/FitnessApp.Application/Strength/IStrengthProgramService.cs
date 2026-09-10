namespace FitnessApp.Application.Strength;

public sealed record ExerciseInput(Guid? Id, string? Name, decimal Weight, int Sets, int Repetitions, string? Note);
public sealed record WorkoutInput(Guid? Id, string? Name, IReadOnlyList<ExerciseInput>? Exercises);
public sealed record ProgramInput(string? Name, Guid? Version, IReadOnlyList<WorkoutInput>? Workouts);
public sealed record ExerciseData(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note);
public sealed record ActiveExerciseData(Guid Id, string Name, decimal Weight, int Sets, int Repetitions, string? Note,
    decimal? PreviousWeight, int? PreviousSets, int? PreviousRepetitions);
public sealed record WorkoutData(Guid Id, string Name, IReadOnlyList<ExerciseData> Exercises);
public sealed record ProgramData(Guid Id, string Name, Guid Version, IReadOnlyList<WorkoutData> Workouts);
public sealed record ScheduleEntryData(DayOfWeek DayOfWeek, Guid? WorkoutId);
public sealed record ScheduleData(Guid Version, IReadOnlyList<ScheduleEntryData> Entries);
public sealed record PlannedWorkoutData(Guid ProgramId, string ProgramName, Guid WorkoutId, string WorkoutName,
    IReadOnlyList<ActiveExerciseData> Exercises);
public sealed record StrengthOverviewData(IReadOnlyList<ProgramData> Programs, PlannedWorkoutData? Today);
public sealed record CompletedExerciseInput(Guid? ProgramExerciseId, string? Name, decimal Weight, int Sets,
    int Repetitions, bool IsCompleted);
public sealed record CompletionInput(Guid CompletionId, IReadOnlyList<CompletedExerciseInput>? Exercises);
public enum ProgramStatus { Saved, NotFound, Invalid, Conflict }
public sealed record ProgramResult(ProgramStatus Status, ProgramData? Program = null);

public interface IStrengthProgramService
{
    Task<StrengthOverviewData> GetOverviewAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProgramData>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<ProgramData?> GetAsync(string userId, Guid id, CancellationToken cancellationToken);
    Task<ProgramResult> SaveAsync(string userId, Guid? id, ProgramInput input, CancellationToken cancellationToken);
    Task<ProgramStatus> DeleteAsync(string userId, Guid id, Guid version, CancellationToken cancellationToken);
    Task<ScheduleData?> GetScheduleAsync(string userId, Guid programId, CancellationToken cancellationToken);
    Task<ProgramStatus> SaveScheduleAsync(string userId, Guid programId, Guid version,
        IReadOnlyList<ScheduleEntryData>? entries, CancellationToken cancellationToken);
    Task<PlannedWorkoutData?> GetWorkoutAsync(string userId, Guid programId, Guid workoutId, CancellationToken cancellationToken);
    Task<ProgramStatus> CompleteWorkoutAsync(string userId, Guid programId, Guid workoutId, CompletionInput input,
        CancellationToken cancellationToken);
}
