using FitnessApp.Application.Strength;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Strength;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Strength;

public sealed class StrengthProgramService(FitnessDbContext db, TimeProvider timeProvider) : IStrengthProgramService
{
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();

    public async Task<StrengthOverviewData> GetOverviewAsync(string userId, CancellationToken cancellationToken)
    {
        var programs = await QueryPrograms().Where(program => program.UserId == userId)
            .OrderBy(program => program.CreatedAt).ThenBy(program => program.Id)
            .ToListAsync(cancellationToken);
        var today = Today();
        var occurrenceMoves = await db.CalendarOccurrenceMoves.AsNoTracking()
            .Where(move => move.UserId == userId && move.Kind == CalendarOccurrenceKind.Strength &&
                           (move.OriginalDate == today || move.TargetDate == today))
            .ToListAsync(cancellationToken);
        var movesByOccurrence = occurrenceMoves
            .GroupBy(move => new StrengthOccurrenceKey(move.ScopeId, move.SourceId, move.OriginalDate))
            .ToDictionary(group => group.Key, group => group.First());
        var completedOccurrences = (await db.CompletedWorkouts.AsNoTracking()
                .Where(workout => workout.UserId == userId && workout.ScheduledOccurrenceDate == today)
                .Select(workout => new StrengthOccurrenceKey(workout.ProgramId, workout.WorkoutId,
                    workout.ScheduledOccurrenceDate!.Value))
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var completedMovedToToday = await (from completed in db.CompletedWorkouts.AsNoTracking()
                                            where completed.UserId == userId &&
                                                  completed.ScheduledOccurrenceDate != null
                                            join move in db.CalendarOccurrenceMoves.AsNoTracking()
                                                on new
                                                {
                                                    completed.UserId,
                                                    ScopeId = completed.ProgramId,
                                                    SourceId = completed.WorkoutId,
                                                    OriginalDate = completed.ScheduledOccurrenceDate!.Value
                                                }
                                                equals new { move.UserId, move.ScopeId, move.SourceId, move.OriginalDate }
                                            where move.Kind == CalendarOccurrenceKind.Strength && move.TargetDate == today
                                            select new StrengthOccurrenceKey(completed.ProgramId, completed.WorkoutId,
                                                completed.ScheduledOccurrenceDate!.Value))
            .ToListAsync(cancellationToken);
        completedOccurrences.UnionWith(completedMovedToToday);

        var candidates = new List<PlannedWorkoutData>();
        foreach (var program in programs)
        {
            foreach (var entry in program.Schedule.Where(entry => entry.DayOfWeek == today.DayOfWeek &&
                         entry.WorkoutId is not null))
            {
                var workout = program.Workouts.FirstOrDefault(candidate => candidate.Id == entry.WorkoutId);
                if (workout is null)
                {
                    continue;
                }

                var key = new StrengthOccurrenceKey(program.Id, workout.Id, today);
                if (!movesByOccurrence.ContainsKey(key) && !completedOccurrences.Contains(key))
                {
                    candidates.Add(PlannedWorkout(program, workout));
                }
            }
        }

        foreach (var move in occurrenceMoves.Where(move => move.TargetDate == today))
        {
            var program = programs.FirstOrDefault(candidate => candidate.Id == move.ScopeId);
            var workout = program?.Workouts.FirstOrDefault(candidate => candidate.Id == move.SourceId);
            var key = new StrengthOccurrenceKey(move.ScopeId, move.SourceId, move.OriginalDate);
            if (program is not null && workout is not null &&
                program.Schedule.Any(entry => entry.DayOfWeek == move.OriginalDate.DayOfWeek &&
                                              entry.WorkoutId == workout.Id) &&
                !completedOccurrences.Contains(key))
            {
                candidates.Add(PlannedWorkout(program, workout));
            }
        }

        return new StrengthOverviewData(programs.Select(Map).ToArray(), candidates.FirstOrDefault());
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
                CreatedAt = UtcNow()
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

    public async Task<ScheduleData?> GetScheduleAsync(string userId, Guid programId,
        CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(
            candidate => candidate.Id == programId && candidate.UserId == userId, cancellationToken);
        return program is null ? null : new ScheduleData(program.Version, ToWeek(program.Schedule));
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
                                    exercise.ProgramExerciseId != null && exercise.IsCompleted
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
        if (input.CompletionId == Guid.Empty)
        {
            return ProgramStatus.Invalid;
        }

        var existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
        if (existing is not null)
        {
            return MatchesCompletion(existing, programId, workoutId, input) ? ProgramStatus.Saved : ProgramStatus.Conflict;
        }

        var planned = await GetWorkoutAsync(userId, programId, workoutId, cancellationToken);
        if (planned is null)
        {
            return ProgramStatus.NotFound;
        }

        if (input.ScheduledOccurrenceDate is { } scheduledOccurrenceDate &&
            !await CanCompleteScheduledOccurrenceAsync(userId, programId, workoutId, scheduledOccurrenceDate,
                cancellationToken))
        {
            return ProgramStatus.Invalid;
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
            CompletionId = input.CompletionId,
            ProgramId = programId,
            WorkoutId = workoutId,
            ScheduledOccurrenceDate = input.ScheduledOccurrenceDate,
            WorkoutName = planned.WorkoutName,
            CompletedAt = UtcNow(),
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
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ProgramStatus.Saved;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await FindCompletionAsync(userId, input.CompletionId, cancellationToken);
            return existing is not null && MatchesCompletion(existing, programId, workoutId, input)
                ? ProgramStatus.Saved
                : ProgramStatus.Conflict;
        }
    }

    public async Task<CompletedWorkoutData?> GetCompletedWorkoutAsync(string userId, Guid completionId,
        CancellationToken cancellationToken)
    {
        var completed = await db.CompletedWorkouts.AsNoTracking()
            .Include(workout => workout.Exercises)
            .SingleOrDefaultAsync(workout => workout.Id == completionId && workout.UserId == userId, cancellationToken);
        return completed is null
            ? null
            : new CompletedWorkoutData(
                completed.Id,
                CopenhagenDate(completed.CompletedAt),
                completed.WorkoutName,
                completed.Exercises.OrderBy(exercise => exercise.Position)
                    .Select(exercise => new CompletedWorkoutExerciseData(exercise.Name, exercise.Weight, exercise.Sets,
                        exercise.Repetitions, exercise.IsCompleted))
                    .ToArray());
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

    private static PlannedWorkoutData PlannedWorkout(StrengthProgram program, ProgramWorkout workout) => new(
        program.Id,
        program.Name,
        workout.Id,
        workout.Name,
        workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise => MapActive(exercise)).ToArray());

    private Task<CompletedWorkout?> FindCompletionAsync(string userId, Guid completionId, CancellationToken cancellationToken) =>
        db.CompletedWorkouts.AsNoTracking().Include(workout => workout.Exercises).SingleOrDefaultAsync(
            workout => workout.UserId == userId && workout.CompletionId == completionId, cancellationToken);

    private async Task<bool> CanCompleteScheduledOccurrenceAsync(string userId, Guid programId, Guid workoutId,
        DateOnly scheduledOccurrenceDate, CancellationToken cancellationToken)
    {
        var isScheduled = await (from program in db.StrengthPrograms.AsNoTracking()
                                 join schedule in db.ProgramScheduleEntries.AsNoTracking() on program.Id equals schedule.ProgramId
                                 join workout in db.ProgramWorkouts.AsNoTracking() on schedule.WorkoutId equals (Guid?)workout.Id
                                 where program.Id == programId && program.UserId == userId && workout.Id == workoutId &&
                                       workout.ProgramId == program.Id &&
                                       schedule.DayOfWeek == scheduledOccurrenceDate.DayOfWeek
                                 select schedule.Id)
            .AnyAsync(cancellationToken);
        return isScheduled && !await db.CompletedWorkouts.AsNoTracking().AnyAsync(workout =>
            workout.UserId == userId && workout.ProgramId == programId && workout.WorkoutId == workoutId &&
            workout.ScheduledOccurrenceDate == scheduledOccurrenceDate, cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenTimeZone).DateTime);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateOnly CopenhagenDate(DateTime utc) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), CopenhagenTimeZone));

    private static TimeZoneInfo FindCopenhagenTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }

    private static bool MatchesCompletion(CompletedWorkout completed, Guid programId, Guid workoutId, CompletionInput input) =>
        completed.ProgramId == programId && completed.WorkoutId == workoutId &&
        completed.ScheduledOccurrenceDate == input.ScheduledOccurrenceDate && input.Exercises is { } exercises &&
        completed.Exercises.OrderBy(exercise => exercise.Position).Zip(exercises).All(pair =>
            pair.First.ProgramExerciseId == pair.Second.ProgramExerciseId &&
            pair.First.Weight == pair.Second.Weight &&
            pair.First.Sets == pair.Second.Sets &&
            pair.First.Repetitions == pair.Second.Repetitions &&
            pair.First.IsCompleted == pair.Second.IsCompleted) &&
        completed.Exercises.Count == exercises.Count;

    private readonly record struct StrengthOccurrenceKey(Guid ProgramId, Guid WorkoutId, DateOnly OriginalDate);
}
