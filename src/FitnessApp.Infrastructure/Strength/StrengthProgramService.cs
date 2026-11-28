using FitnessApp.Application.Strength;
using FitnessApp.Domain.Calendar;
using FitnessApp.Domain.Strength;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Strength;

public sealed class StrengthProgramService(FitnessDbContext db, TimeProvider timeProvider) : IStrengthProgramService
{
    private const int VolumeMilestoneKg = 1000;
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();

    public async Task<IReadOnlyList<CatalogExercise>> GetCatalogAsync(CancellationToken cancellationToken) =>
        await db.StrengthCatalog.AsNoTracking().OrderBy(exercise => exercise.Name).ToListAsync(cancellationToken);

    public Task<IReadOnlyList<StrengthExerciseProgressionData>> GetProgressionAsync(string userId, int days,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var firstDay = CopenhagenDate(now).AddDays(1 - days);
        var start = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), CopenhagenTimeZone);
        return GetDailyProgressionAsync(userId, start, now, cancellationToken);
    }

    public async Task<StrengthMilestonesData> GetMilestonesAsync(string userId, CancellationToken cancellationToken)
    {
        var history = await GetDailyProgressionAsync(userId, DateTime.MinValue, UtcNow(), cancellationToken);
        decimal volume = 0;
        DateOnly? volumeEarnedOn = null;
        foreach (var day in history.SelectMany(exercise => exercise.Days).Where(day => day.VolumeKg.HasValue)
                     .OrderBy(day => day.Date))
        {
            volume += day.VolumeKg!.Value;
            if (volume >= VolumeMilestoneKg)
            {
                volumeEarnedOn ??= day.Date;
            }
        }

        var recordEarnedOn = history.Select(exercise =>
        {
            // Day one establishes the baseline. Improving it later earns the badge once.
            var baseline = RecordMeasurement(exercise.Days[0]);
            return exercise.Days.Skip(1).FirstOrDefault(day => RecordMeasurement(day) > baseline)?.Date;
        }).Min();

        return new StrengthMilestonesData(volume, VolumeMilestoneKg, volumeEarnedOn, history.Count > 0, recordEarnedOn);
    }

    private static decimal RecordMeasurement(StrengthProgressionDayData day) =>
        day.MaxWeight ?? day.MaxDurationSeconds!.Value;

    private async Task<IReadOnlyList<StrengthExerciseProgressionData>> GetDailyProgressionAsync(string userId,
        DateTime start, DateTime now, CancellationToken cancellationToken)
    {
        var measurements = await (from workout in db.CompletedWorkouts.AsNoTracking()
                                  where workout.UserId == userId && workout.CompletedAt >= start && workout.CompletedAt <= now
                                  from exercise in workout.Exercises
                                  where exercise.IsCompleted
                                  orderby workout.CompletedAt descending, workout.Id descending, exercise.Position
                                  select new
                                  {
                                      workout.CompletedAt, workout.WorkoutName, exercise.Name, exercise.Id,
                                      exercise.CatalogExerciseId, exercise.ProgramExerciseId, exercise.DurationSeconds,
                                      exercise.Weight, exercise.Sets, exercise.Repetitions
                                  }).ToListAsync(cancellationToken);

        return measurements.GroupBy(item =>
                HistoryKey.For(item.CatalogExerciseId, item.ProgramExerciseId ?? item.Id, item.DurationSeconds))
            .Select(exercise =>
            {
                var latest = exercise.First();
                var key = exercise.Key;
                var identity = key.CatalogId is { } id ? $"catalog:{id}" : $"manual:{key.ProgramExerciseId:N}";
                var name = key.CatalogId is null ? $"{latest.Name} · {latest.WorkoutName}" : latest.Name;
                var history = exercise.GroupBy(item => CopenhagenDate(item.CompletedAt))
                    .OrderBy(day => day.Key)
                    .Select(day => key.IsTimed
                        ? new StrengthProgressionDayData(day.Key, null, null, null, day.Max(item => item.DurationSeconds))
                        : new StrengthProgressionDayData(day.Key, day.Max(item => item.Weight),
                            day.Max(item => item.Repetitions), day.Sum(item => item.Weight * item.Sets * item.Repetitions),
                            null))
                    .ToArray();
                return new StrengthExerciseProgressionData($"{identity}:{(key.IsTimed ? "time" : "weight")}",
                    name, key.IsTimed, history);
            })
            .OrderBy(exercise => exercise.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(exercise => exercise.Key, StringComparer.Ordinal)
            .ToArray();
    }

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

        if (!IsValidProgramStructure(input))
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

        var previousCatalogIds = program?.Workouts.SelectMany(workout => workout.Exercises)
            .ToDictionary(exercise => exercise.Id, exercise => exercise.CatalogExerciseId) ?? [];
        var exercises = input.Workouts!.SelectMany(workout => workout.Exercises!);
        // Older clients omit the catalog and duration fields. Do not let such an edit
        // silently convert an existing catalog exercise into a legacy manual entry.
        if (exercises.Any(exercise => exercise.Id is { } exerciseId && exercise.CatalogExerciseId is null &&
            previousCatalogIds.GetValueOrDefault(exerciseId) is not null))
        {
            return new ProgramResult(ProgramStatus.Invalid);
        }

        var catalogIds = exercises.Select(exercise => exercise.CatalogExerciseId).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        var catalog = catalogIds.Length == 0
            ? new Dictionary<string, CatalogExercise>(StringComparer.Ordinal)
            : await db.StrengthCatalog.AsNoTracking().Where(exercise => catalogIds.Contains(exercise.Id))
                .ToDictionaryAsync(exercise => exercise.Id, StringComparer.Ordinal, cancellationToken);
        if (exercises.Any(exercise => !IsValidProgramExercise(exercise, catalog)))
        {
            return new ProgramResult(ProgramStatus.Invalid);
        }

        var programId = id ?? Guid.NewGuid();
        var nextVersion = Guid.NewGuid();
        var workouts = BuildWorkouts(programId, input.Workouts!, catalog, previousCatalogIds);
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
        if (await FindWorkoutAsync(userId, programId, workoutId, cancellationToken) is not { } template)
        {
            return null;
        }

        var (program, workout) = template;
        var keys = workout.Exercises.Select(exercise =>
            HistoryKey.For(exercise.CatalogExerciseId, exercise.Id, exercise.DurationSeconds)).ToHashSet();
        var weightCatalogIds = keys.Where(key => !key.IsTimed).Select(key => key.CatalogId).OfType<string>().ToArray();
        var timedCatalogIds = keys.Where(key => key.IsTimed).Select(key => key.CatalogId).OfType<string>().ToArray();
        var manualIds = keys.Select(key => key.ProgramExerciseId).OfType<Guid>().ToArray();
        var previous = from exercise in db.CompletedWorkoutExercises.AsNoTracking()
                       join completed in db.CompletedWorkouts.AsNoTracking() on exercise.CompletedWorkoutId equals completed.Id
                       where completed.UserId == userId && exercise.IsCompleted &&
                             (exercise.DurationSeconds == null && weightCatalogIds.Contains(exercise.CatalogExerciseId!) ||
                              exercise.DurationSeconds != null && timedCatalogIds.Contains(exercise.CatalogExerciseId!) ||
                              exercise.CatalogExerciseId == null && exercise.ProgramExerciseId != null &&
                              completed.ProgramId == programId && completed.WorkoutId == workoutId &&
                              manualIds.Contains(exercise.ProgramExerciseId.Value))
                       orderby completed.CompletedAt descending, completed.Id descending, exercise.Position
                       select new
                       {
                           exercise.ProgramExerciseId, exercise.CatalogExerciseId, exercise.Weight,
                           exercise.Sets, exercise.Repetitions, exercise.DurationSeconds
                       };

        var latest = new Dictionary<HistoryKey, PreviousPerformance>();
        if (keys.Count > 0)
        {
            await foreach (var item in previous.AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                var key = HistoryKey.For(item.CatalogExerciseId, item.ProgramExerciseId, item.DurationSeconds);
                if (keys.Contains(key))
                {
                    latest.TryAdd(key, new PreviousPerformance(item.Weight, item.Sets, item.Repetitions, item.DurationSeconds));
                }
                if (latest.Count == keys.Count)
                {
                    break;
                }
            }
        }

        return new PlannedWorkoutData(program.Id, program.Name, workout.Id, workout.Name,
            workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise =>
            {
                var key = HistoryKey.For(exercise.CatalogExerciseId, exercise.Id, exercise.DurationSeconds);
                latest.TryGetValue(key, out var performance);
                return MapActive(exercise, key.IsTimed ? null : performance?.Weight, performance?.Sets,
                    key.IsTimed ? null : performance?.Repetitions,
                    performance?.DurationSeconds);
            }).ToArray());
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

        if (await FindWorkoutAsync(userId, programId, workoutId, cancellationToken) is not { } template)
        {
            return ProgramStatus.NotFound;
        }
        var planned = PlannedWorkout(template.Program, template.Workout);

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
                !StrengthRules.IsValidMeasurement(source.Name, exercise.Weight, exercise.Sets, exercise.Repetitions,
                    null, source.DurationSeconds is not null, exercise.DurationSeconds)) ||
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
                CatalogExerciseId = plannedByExerciseId[exercise.ProgramExerciseId!.Value].CatalogExerciseId,
                DurationSeconds = exercise.DurationSeconds,
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
                        exercise.Repetitions, exercise.IsCompleted)
                    {
                        CatalogExerciseId = exercise.CatalogExerciseId,
                        DurationSeconds = exercise.DurationSeconds
                    })
                    .ToArray());
    }

    private IQueryable<StrengthProgram> QueryPrograms() => db.StrengthPrograms.AsNoTracking()
        .Include(program => program.Workouts).ThenInclude(workout => workout.Exercises)
        .Include(program => program.Schedule);

    private async Task<(StrengthProgram Program, ProgramWorkout Workout)?> FindWorkoutAsync(
        string userId, Guid programId, Guid workoutId, CancellationToken cancellationToken)
    {
        var program = await QueryPrograms().SingleOrDefaultAsync(candidate => candidate.Id == programId && candidate.UserId == userId,
            cancellationToken);
        return program?.Workouts.SingleOrDefault(candidate => candidate.Id == workoutId) is { } workout
            ? (program, workout)
            : null;
    }

    private static bool IsValidProgramStructure(ProgramInput input) =>
        StrengthRules.IsValidName(input.Name) && input.Workouts is { Count: <= StrengthRules.MaxWorkouts } &&
        input.Workouts.All(workout => workout is not null && StrengthRules.IsValidName(workout.Name) &&
            workout.Exercises is { Count: >= 1 and <= StrengthRules.MaxExercises } &&
            workout.Exercises.All(exercise => exercise is not null));

    private static bool IsValidProgramExercise(ExerciseInput exercise, IReadOnlyDictionary<string, CatalogExercise> catalog)
    {
        CatalogExercise? definition = null;
        if (exercise.CatalogExerciseId is { } id && !catalog.TryGetValue(id, out definition))
        {
            return false;
        }

        return StrengthRules.IsValidMeasurement(definition?.Name ?? exercise.Name, exercise.Weight, exercise.Sets,
            exercise.Repetitions, exercise.Note, definition?.IsTimed == true, exercise.DurationSeconds);
    }

    private static List<ProgramWorkout> BuildWorkouts(Guid programId, IReadOnlyList<WorkoutInput> input,
        IReadOnlyDictionary<string, CatalogExercise> catalog, IReadOnlyDictionary<Guid, string?> previousCatalogIds) =>
        input.Select((workout, workoutPosition) => new ProgramWorkout
        {
            Id = workout.Id ?? Guid.NewGuid(),
            ProgramId = programId,
            Name = workout.Name!.Trim(),
            Position = workoutPosition,
            Exercises = workout.Exercises!.Select((exercise, exercisePosition) => new ProgramExercise
            {
                Id = exercise.Id is { } previousId && previousCatalogIds[previousId] == exercise.CatalogExerciseId
                    ? previousId
                    : Guid.NewGuid(),
                WorkoutId = workout.Id ?? Guid.Empty,
                Name = exercise.CatalogExerciseId is { } id ? catalog[id].Name : exercise.Name!.Trim(),
                Weight = exercise.Weight,
                Sets = exercise.Sets,
                Repetitions = exercise.Repetitions,
                Note = string.IsNullOrWhiteSpace(exercise.Note) ? null : exercise.Note.Trim(),
                CatalogExerciseId = exercise.CatalogExerciseId,
                DurationSeconds = exercise.DurationSeconds,
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
        exercise.Sets, exercise.Repetitions, exercise.Note)
    {
        CatalogExerciseId = exercise.CatalogExerciseId,
        DurationSeconds = exercise.DurationSeconds
    };

    private static ActiveExerciseData MapActive(ProgramExercise exercise, decimal? previousWeight = null,
        int? previousSets = null, int? previousRepetitions = null, int? previousDurationSeconds = null) =>
        new(exercise.Id, exercise.Name, exercise.Weight, exercise.Sets, exercise.Repetitions, exercise.Note,
            previousWeight, previousSets, previousRepetitions)
        {
            CatalogExerciseId = exercise.CatalogExerciseId,
            DurationSeconds = exercise.DurationSeconds,
            PreviousDurationSeconds = previousDurationSeconds
        };

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
            pair.Second is not null && pair.First.ProgramExerciseId == pair.Second.ProgramExerciseId &&
            pair.First.Weight == pair.Second.Weight &&
            pair.First.Sets == pair.Second.Sets &&
            pair.First.Repetitions == pair.Second.Repetitions &&
            pair.First.DurationSeconds == pair.Second.DurationSeconds &&
            pair.First.IsCompleted == pair.Second.IsCompleted) &&
        completed.Exercises.Count == exercises.Count;

    private readonly record struct StrengthOccurrenceKey(Guid ProgramId, Guid WorkoutId, DateOnly OriginalDate);

    private readonly record struct HistoryKey(string? CatalogId, Guid? ProgramExerciseId, bool IsTimed)
    {
        public static HistoryKey For(string? catalogId, Guid? programExerciseId, int? durationSeconds) =>
            new(catalogId, catalogId is null ? programExerciseId : null, durationSeconds is not null);
    }

    private sealed record PreviousPerformance(decimal Weight, int Sets, int Repetitions, int? DurationSeconds);
}
