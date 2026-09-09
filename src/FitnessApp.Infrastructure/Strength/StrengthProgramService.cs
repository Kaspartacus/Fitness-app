using FitnessApp.Application.Strength;
using FitnessApp.Domain.Strength;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Strength;

public sealed class StrengthProgramService(FitnessDbContext db) : IStrengthProgramService
{
    public async Task<IReadOnlyList<ProgramData>> ListAsync(string userId, CancellationToken cancellationToken) =>
        (await db.StrengthPrograms.AsNoTracking().Where(p => p.UserId == userId)
            .Include(p => p.Exercises).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<ProgramData?> GetAsync(string userId, Guid id, CancellationToken cancellationToken)
    {
        var program = await db.StrengthPrograms.AsNoTracking().Include(p => p.Exercises)
            .SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);
        return program is null ? null : Map(program);
    }

    public async Task<ProgramResult> SaveAsync(string userId, Guid? id, ProgramInput input, CancellationToken cancellationToken)
    {
        var program = id is null ? null : await db.StrengthPrograms.AsNoTracking().Include(p => p.Exercises)
            .SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);
        if (id is not null && program is null) return new(ProgramStatus.NotFound);
        if (!StrengthRules.IsValidName(input.Name) || input.Exercises is not { Count: >= 1 and <= 50 } ||
            input.Exercises.Any(e => e is null || !StrengthRules.IsValidExercise(e.Name, e.Sets, e.Repetitions)))
            return new(ProgramStatus.Invalid);
        var suppliedIds = input.Exercises.Where(e => e.Id is not null).Select(e => e.Id!.Value).ToArray();
        if (suppliedIds.Distinct().Count() != suppliedIds.Length ||
            suppliedIds.Any(exerciseId => program is null || !program.Exercises.Any(e => e.Id == exerciseId)))
            return new(ProgramStatus.Invalid);
        if (program is not null && input.Version != program.Version) return new(ProgramStatus.Conflict);

        var programId = id ?? Guid.NewGuid();
        var nextVersion = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (program is null)
        {
            db.StrengthPrograms.Add(new StrengthProgram
            {
                Id = programId,
                UserId = userId,
                Name = input.Name!.Trim(),
                Version = nextVersion,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            var affected = await db.StrengthPrograms
                .Where(candidate => candidate.Id == programId && candidate.UserId == userId && candidate.Version == input.Version)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.Name, input.Name!.Trim())
                    .SetProperty(candidate => candidate.Version, nextVersion), cancellationToken);
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(ProgramStatus.Conflict);
            }

            await db.ProgramExercises.Where(exercise => exercise.ProgramId == programId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var exercises = new List<ProgramExercise>(input.Exercises.Count);
        for (var position = 0; position < input.Exercises.Count; position++)
        {
            var inputExercise = input.Exercises[position];
            exercises.Add(new ProgramExercise
            {
                Id = inputExercise.Id ?? Guid.NewGuid(),
                ProgramId = programId,
                Name = inputExercise.Name!.Trim(),
                Sets = inputExercise.Sets,
                Repetitions = inputExercise.Repetitions,
                IsWarmUp = inputExercise.IsWarmUp,
                Position = position
            });
        }

        db.ProgramExercises.AddRange(exercises);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(ProgramStatus.Saved, new ProgramData(programId, input.Name!.Trim(), nextVersion,
                exercises.OrderBy(e => e.Position).Select(e => new ExerciseData(e.Id, e.Name, e.Sets, e.Repetitions, e.IsWarmUp)).ToArray()));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(ProgramStatus.Conflict);
        }
    }

    public async Task<ProgramStatus> DeleteAsync(string userId, Guid id, Guid version, CancellationToken cancellationToken)
    {
        var program = await db.StrengthPrograms.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);
        if (program is null) return ProgramStatus.NotFound;
        if (program.Version != version) return ProgramStatus.Conflict;
        var affected = await db.StrengthPrograms
            .Where(candidate => candidate.Id == id && candidate.UserId == userId && candidate.Version == version)
            .ExecuteDeleteAsync(cancellationToken);
        return affected == 1 ? ProgramStatus.Saved : ProgramStatus.Conflict;
    }

    private static ProgramData Map(StrengthProgram program) => new(program.Id, program.Name, program.Version,
        program.Exercises.OrderBy(e => e.Position).Select(e => new ExerciseData(e.Id, e.Name, e.Sets, e.Repetitions, e.IsWarmUp)).ToArray());
}
