namespace FitnessApp.Application.Strength;

public sealed record ExerciseInput(Guid? Id, string? Name, int Sets, int Repetitions, bool IsWarmUp);
public sealed record ProgramInput(string? Name, Guid? Version, IReadOnlyList<ExerciseInput>? Exercises);
public sealed record ExerciseData(Guid Id, string Name, int Sets, int Repetitions, bool IsWarmUp);
public sealed record ProgramData(Guid Id, string Name, Guid Version, IReadOnlyList<ExerciseData> Exercises);
public enum ProgramStatus { Saved, NotFound, Invalid, Conflict }
public sealed record ProgramResult(ProgramStatus Status, ProgramData? Program = null);

public interface IStrengthProgramService
{
    Task<IReadOnlyList<ProgramData>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<ProgramData?> GetAsync(string userId, Guid id, CancellationToken cancellationToken);
    Task<ProgramResult> SaveAsync(string userId, Guid? id, ProgramInput input, CancellationToken cancellationToken);
    Task<ProgramStatus> DeleteAsync(string userId, Guid id, Guid version, CancellationToken cancellationToken);
}
