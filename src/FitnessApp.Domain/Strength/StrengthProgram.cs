namespace FitnessApp.Domain.Strength;

public sealed class StrengthProgram
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ProgramExercise> Exercises { get; set; } = [];
}

public sealed class ProgramExercise
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = "";
    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public bool IsWarmUp { get; set; }
    public int Position { get; set; }
}

public static class StrengthRules
{
    public static bool IsValidName(string? value) => value?.Trim().Length is >= 1 and <= 100;
    public static bool IsValidExercise(string? name, int sets, int repetitions) =>
        IsValidName(name) && sets is >= 1 and <= 10 && repetitions is >= 1 and <= 30;
}
