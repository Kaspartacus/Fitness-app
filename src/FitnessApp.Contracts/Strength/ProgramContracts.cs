namespace FitnessApp.Contracts.Strength;

public sealed class SaveProgramRequest
{
    public string? Name { get; set; } = "";
    public Guid? Version { get; set; }
    public List<ExerciseRequest>? Exercises { get; set; } = [];
}

public sealed class ExerciseRequest
{
    public Guid? Id { get; set; }
    public string? Name { get; set; } = "";
    public int Sets { get; set; } = 3;
    public int Repetitions { get; set; } = 10;
    public bool IsWarmUp { get; set; }
}

public sealed record ExerciseResponse(Guid Id, string Name, int Sets, int Repetitions, bool IsWarmUp);
public sealed record ProgramResponse(Guid Id, string Name, Guid Version, IReadOnlyList<ExerciseResponse> Exercises);
