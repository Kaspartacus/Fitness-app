using FitnessApp.Application.Strength;
using FitnessApp.Domain.Strength;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Strength;

public sealed class StrengthProgramService(FitnessDbContext db) : IStrengthProgramService
{
    public async Task<StrengthOverviewData> GetOverviewAsync(string userId, CancellationToken cancellationToken)
    {
        var programs = await QueryPrograms().Where(program => program.UserId == userId)
            .OrderBy(program => program.CreatedAt).ThenBy(program => program.Id)
            .ToListAsync(cancellationToken);
        var today = programs.SelectMany(program => program.Schedule
                .Where(entry => entry.DayOfWeek == DateTime.Today.DayOfWeek && entry.WorkoutId is not null)
                .Select(entry => new { Program = program, Entry = entry }))
            .Select(item => item.Entry.WorkoutId is { } workoutId
                ? item.Program.Workouts.FirstOrDefault(workout => workout.Id == workoutId) is { } workout
                    ? new PlannedWorkoutData(item.Program.Id, item.Program.Name, workout.Id, workout.Name,
                        workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise => MapActive(exercise)).ToArray())
                    : null
                : null)
            .FirstOrDefault(workout => workout is not null);
        return new StrengthOverviewData(programs.Select(Map).ToArray(), today);
    }

    public async Task<IReadOnlyList<ProgramData>> ListAsync(string userId, CancellationToken cancellationToken) =>
        (await QueryPrograms().Where(program => program.UserId == userId)
            .OrderBy(program => program.CreatedAt).ThenBy(program => program.Id)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<ProgramData?> GetAsync(string userId, Guid id, CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.UserId == userId, cancellationToken);
        return program is null ? null : Map(program);
    }

    public async Task<ProgramResult> SaveAsync(string userId, Guid? id, ProgramInput input, CancellationToken cancellationToken)
    {
        var program = id is null ? null : await QueryPrograms().SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.UserId == userId, cancellationToken);
        if (id is not null && program is null)
        {
            return new ProgramResult(ProgramStatus.NotFound);
        }

        if (!IsValidProgram(input))
        {
            return new ProgramResult(ProgramStatus.Invalid);
        }

        var suppliedWorkoutIds = input.Workouts!.Where(workout => workout.Id is not null)
            .Select(workout => workout.Id!.Value).ToArray();
        if (suppliedWorkoutIds.Distinct().Count() != suppliedWorkoutIds.Length ||
            suppliedWorkoutIds.Any(workoutId => program is null || !program.Workouts.Any(workout => workout.Id == workoutId)))
        {
            return new ProgramResult(ProgramStatus.Invalid);
        }

        var suppliedExerciseIds = input.Workouts!.SelectMany(workout => workout.Exercises!.Where(exercise => exercise.Id is not null)
            .Select(exercise => new { WorkoutId = workout.Id, ExerciseId = exercise.Id!.Value })).ToArray();
        if (suppliedExerciseIds.Select(item => item.ExerciseId).Distinct().Count() != suppliedExerciseIds.Length ||
            suppliedExerciseIds.Any(item => item.WorkoutId is null || program is null ||
                !program.Workouts.Any(workout => workout.Id == item.WorkoutId &&
                    workout.Exercises.Any(exercise => exercise.Id == item.ExerciseId))))
        {
            return new ProgramResult(ProgramStatus.Invalid);
        }

        if (program is not null && input.Version != program.Version)
        {
            return new ProgramResult(ProgramStatus.Conflict);
        }

        var programId = id ?? Guid.NewGuid();
        var nextVersion = Guid.NewGuid();
        var workouts = BuildWorkouts(programId, input.Workouts!);
        var preservedSchedule = program?.Schedule
            .Where(entry => entry.WorkoutId is { } workoutId && workouts.Any(workout => workout.Id == workoutId))
            .Select(entry => new ProgramScheduleEntry
            {
                Id = Guid.NewGuid(), ProgramId = programId, DayOfWeek = entry.DayOfWeek, WorkoutId = entry.WorkoutId
            }).ToArray() ?? [];

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
                return new ProgramResult(ProgramStatus.Conflict);
            }

            await db.ProgramScheduleEntries.Where(entry => entry.ProgramId == programId).ExecuteDeleteAsync(cancellationToken);
            await db.ProgramWorkouts.Where(workout => workout.ProgramId == programId).ExecuteDeleteAsync(cancellationToken);
        }

        db.ProgramWorkouts.AddRange(workouts);
        db.ProgramScheduleEntries.AddRange(preservedSchedule);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProgramResult(ProgramStatus.Saved,
                new ProgramData(programId, input.Name!.Trim(), nextVersion, workouts.Select(Map).ToArray()));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new ProgramResult(ProgramStatus.Conflict);
        }
    }

    public async Task<ProgramStatus> DeleteAsync(string userId, Guid id, Guid version, CancellationToken cancellationToken)
    {
        var program = await db.StrengthPrograms.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.UserId == userId, cancellationToken);
        if (program is null)
        {
            return ProgramStatus.NotFound;
        }

        if (program.Version != version)
        {
            return ProgramStatus.Conflict;
        }

        var affected = await db.StrengthPrograms.Where(candidate => candidate.Id == id && candidate.UserId == userId &&
                candidate.Version == version).ExecuteDeleteAsync(cancellationToken);
        return affected == 1 ? ProgramStatus.Saved : ProgramStatus.Conflict;
    }

    public async Task<IReadOnlyList<ScheduleEntryData>?> GetScheduleAsync(string userId, Guid programId,
        CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(
            candidate => candidate.Id == programId && candidate.UserId == userId, cancellationToken);
        return program is null ? null : ToWeek(program.Schedule);
    }

    public async Task<ProgramStatus> SaveScheduleAsync(string userId, Guid programId, Guid version,
        IReadOnlyList<ScheduleEntryData>? entries, CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(
            candidate => candidate.Id == programId && candidate.UserId == userId, cancellationToken);
        if (program is null)
        {
            return ProgramStatus.NotFound;
        }

        if (version != program.Version)
        {
            return ProgramStatus.Conflict;
        }

        if (entries is not { Count: 7 } || entries.Any(entry => entry is null ||
                entry.DayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday ||
                entry.WorkoutId is { } workoutId && !program.Workouts.Any(workout => workout.Id == workoutId)) ||
            entries.Select(entry => entry.DayOfWeek).Distinct().Count() != 7)
        {
            return ProgramStatus.Invalid;
        }

        var nextVersion = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await db.StrengthPrograms.Where(candidate => candidate.Id == programId && candidate.UserId == userId &&
                candidate.Version == version).ExecuteUpdateAsync(updates => updates.SetProperty(candidate => candidate.Version, nextVersion), cancellationToken);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ProgramStatus.Conflict;
        }

        await db.ProgramScheduleEntries.Where(entry => entry.ProgramId == programId).ExecuteDeleteAsync(cancellationToken);
        db.ProgramScheduleEntries.AddRange(entries.Where(entry => entry.WorkoutId is not null).Select(entry => new ProgramScheduleEntry
        {
            Id = Guid.NewGuid(), ProgramId = programId, DayOfWeek = entry.DayOfWeek, WorkoutId = entry.WorkoutId
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProgramStatus.Saved;
    }

    public async Task<PlannedWorkoutData?> GetWorkoutAsync(string userId, Guid programId, Guid workoutId,
        CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(candidate => candidate.Id == programId && candidate.UserId == userId,
            cancellationToken);
        var workout = program?.Workouts.SingleOrDefault(candidate => candidate.Id == workoutId);
        if (workout is null || program is null)
        {
            return null;
        }

        var previous = await (from exercise in db.CompletedWorkoutExercises.AsNoTracking()
                              join completed in db.CompletedWorkouts.AsNoTracking() on exercise.CompletedWorkoutId equals completed.Id
                              where completed.UserId == userId && completed.ProgramId == programId && completed.WorkoutId == workoutId &&
                                    exercise.ProgramExerciseId != null
                              orderby completed.CompletedAt descending
                              select new { ProgramExerciseId = exercise.ProgramExerciseId!.Value, exercise.Weight, exercise.Sets, exercise.Repetitions })
            .ToListAsync(cancellationToken);
        var latest = previous.GroupBy(item => item.ProgramExerciseId).ToDictionary(group => group.Key, group => group.First());
        return new PlannedWorkoutData(program.Id, program.Name, workout.Id, workout.Name,
            workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise => MapActive(exercise,
                latest.TryGetValue(exercise.Id, out var performance) ? performance.Weight : null,
                latest.TryGetValue(exercise.Id, out performance) ? performance.Sets : null,
                latest.TryGetValue(exercise.Id, out performance) ? performance.Repetitions : null)).ToArray());
    }

    public async Task<ProgramStatus> CompleteWorkoutAsync(string userId, Guid programId, Guid workoutId, CompletionInput input,
        CancellationToken cancellationToken)
    {
        var planned = await GetWorkoutAsync(userId, programId, workoutId, cancellationToken);
        if (planned is null)
        {
            return ProgramStatus.NotFound;
        }

        var plannedByExerciseId = planned.Exercises.ToDictionary(exercise => exercise.Id);
        if (input.Exercises is not { Count: > 0 } || input.Exercises.Count != planned.Exercises.Count ||
            input.Exercises.Any(exercise => exercise is null || exercise.ProgramExerciseId is null ||
                !plannedByExerciseId.TryGetValue(exercise.ProgramExerciseId.Value, out var source) ||
                !StrengthRules.IsValidExercise(source.Name, exercise.Weight, exercise.Sets, exercise.Repetitions, null)) ||
            input.Exercises.Select(exercise => exercise.ProgramExerciseId!.Value).Distinct().Count() != input.Exercises.Count)
        {
            return ProgramStatus.Invalid;
        }

        var completed = new CompletedWorkout
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProgramId = programId,
            WorkoutId = workoutId,
            WorkoutName = planned.WorkoutName,
            CompletedAt = DateTime.UtcNow,
            Exercises = input.Exercises.Select((exercise, position) => new CompletedWorkoutExercise
            {
                Id = Guid.NewGuid(),
                ProgramExerciseId = exercise.ProgramExerciseId,
                Name = plannedByExerciseId[exercise.ProgramExerciseId!.Value].Name,
                Weight = exercise.Weight,
                Sets = exercise.Sets,
                Repetitions = exercise.Repetitions,
                IsCompleted = exercise.IsCompleted,
                Position = position
            }).ToList()
        };
        db.CompletedWorkouts.Add(completed);
        await db.SaveChangesAsync(cancellationToken);
        return ProgramStatus.Saved;
    }

    private IQueryable<StrengthProgram> QueryPrograms() => db.StrengthPrograms.AsNoTracking()
        .Include(program => program.Workouts).ThenInclude(workout => workout.Exercises)
        .Include(program => program.Schedule);

    private static bool IsValidProgram(ProgramInput input) =>
        StrengthRules.IsValidName(input.Name) && input.Workouts is { Count: <= StrengthRules.MaxWorkouts } &&
        input.Workouts.All(workout => workout is not null && StrengthRules.IsValidName(workout.Name) &&
            workout.Exercises is { Count: >= 1 and <= StrengthRules.MaxExercises } && workout.Exercises.All(exercise => exercise is not null &&
                StrengthRules.IsValidExercise(exercise.Name, exercise.Weight, exercise.Sets, exercise.Repetitions, exercise.Note)));

    private static List<ProgramWorkout> BuildWorkouts(Guid programId, IReadOnlyList<WorkoutInput> input) =>
        input.Select((workout, workoutPosition) => new ProgramWorkout
        {
            Id = workout.Id ?? Guid.NewGuid(),
            ProgramId = programId,
            Name = workout.Name!.Trim(),
            Position = workoutPosition,
            Exercises = workout.Exercises!.Select((exercise, exercisePosition) => new ProgramExercise
            {
                Id = exercise.Id ?? Guid.NewGuid(),
                WorkoutId = workout.Id ?? Guid.Empty,
                Name = exercise.Name!.Trim(),
                Weight = exercise.Weight,
                Sets = exercise.Sets,
                Repetitions = exercise.Repetitions,
                Note = string.IsNullOrWhiteSpace(exercise.Note) ? null : exercise.Note.Trim(),
                Position = exercisePosition
            }).ToList()
        }).Select(workout =>
        {
            foreach (var exercise in workout.Exercises)
            {
                exercise.WorkoutId = workout.Id;
            }
            return workout;
        }).ToList();

    private static IReadOnlyList<ScheduleEntryData> ToWeek(IEnumerable<ProgramScheduleEntry> entries) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
            DayOfWeek.Saturday, DayOfWeek.Sunday }
        .Select(day => new ScheduleEntryData(day, entries.SingleOrDefault(entry => entry.DayOfWeek == day)?.WorkoutId)).ToArray();

    private static ProgramData Map(StrengthProgram program) => new(program.Id, program.Name, program.Version,
        program.Workouts.OrderBy(workout => workout.Position).Select(Map).ToArray());

    private static WorkoutData Map(ProgramWorkout workout) => new(workout.Id, workout.Name,
        workout.Exercises.OrderBy(exercise => exercise.Position).Select(Map).ToArray());

    private static ExerciseData Map(ProgramExercise exercise) => new(exercise.Id, exercise.Name, exercise.Weight,
        exercise.Sets, exercise.Repetitions, exercise.Note);

    private static ActiveExerciseData MapActive(ProgramExercise exercise, decimal? previousWeight = null,
        int? previousSets = null, int? previousRepetitions = null) => new(exercise.Id, exercise.Name, exercise.Weight,
        exercise.Sets, exercise.Repetitions, exercise.Note, previousWeight, previousSets, previousRepetitions);
}
