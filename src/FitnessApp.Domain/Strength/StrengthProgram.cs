namespace FitnessApp.Domain.Strength;

public sealed class StrengthProgram
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ProgramWorkout> Workouts { get; set; } = [];
    public List<ProgramScheduleEntry> Schedule { get; set; } = [];
}

public sealed class ProgramWorkout
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public List<ProgramExercise> Exercises { get; set; } = [];
}

public sealed class ProgramExercise
{
    public Guid Id { get; set; }
    public Guid WorkoutId { get; set; }
    public string Name { get; set; } = "";
    public decimal Weight { get; set; }
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public string? Note { get; set; }
    public int Position { get; set; }
}

public sealed class ProgramScheduleEntry
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public Guid? WorkoutId { get; set; }
}

public sealed class CompletedWorkout
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid CompletionId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid WorkoutId { get; set; }
    public string WorkoutName { get; set; } = "";
    public DateTime CompletedAt { get; set; }
    public List<CompletedWorkoutExercise> Exercises { get; set; } = [];
}

public sealed class CompletedWorkoutExercise
{
    public Guid Id { get; set; }
    public Guid CompletedWorkoutId { get; set; }
    public Guid? ProgramExerciseId { get; set; }
    public string Name { get; set; } = "";
    public decimal Weight { get; set; }
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public bool IsCompleted { get; set; }
    public int Position { get; set; }
}

public static class StrengthRules
{
    public const int MaxWorkouts = 12;
    public const int MaxExercises = 50;
    public const int MaxNoteLength = 250;

    public static bool IsValidName(string? value) => value?.Trim().Length is >= 1 and <= 100;

    public static bool IsValidExercise(string? name, decimal weight, int sets, int repetitions, string? note) =>
        IsValidName(name) && weight is >= 0 and <= 1000 && sets is >= 1 and <= 10 &&
        repetitions is >= 1 and <= 30 && note?.Trim().Length is not > MaxNoteLength;
}
