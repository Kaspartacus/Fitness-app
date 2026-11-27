using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Strength;
using FitnessApp.Domain.Strength;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class StrengthProgramTests
{
    private const string Programs = "/api/strength/programs";
    private const string FlatExerciseMigration = "20260910180425_AddExerciseDetails";

    [Fact]
    public void WarmUpBooleanIsRemovedFromTheStrengthModel()
    {
        Assert.Null(typeof(ProgramExercise).GetProperty("IsWarmUp"));
        Assert.Null(typeof(ExerciseRequest).GetProperty("IsWarmUp"));
        Assert.Null(typeof(ExerciseResponse).GetProperty("IsWarmUp"));
    }

    [Fact]
    public async Task MigrationPreservesFlatProgramsByGivingThemOneDefaultWorkout()
    {
        using var storage = new TestAppStorage();
        var programId = Guid.NewGuid();
        var userId = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseSqlite($"Data Source={storage.DatabasePath}")
            .Options;
        await using (var legacy = new FitnessDbContext(options))
        {
            await legacy.GetService<IMigrator>().MigrateAsync(FlatExerciseMigration);
            legacy.Users.Add(new ApplicationUser
            {
                Id = userId,
                DisplayName = "Eksisterende bruger",
                ApprovalStatus = AccountApprovalStatus.Approved,
                UserName = "existing@example.test",
                NormalizedUserName = "EXISTING@EXAMPLE.TEST",
                Email = "existing@example.test",
                NormalizedEmail = "EXISTING@EXAMPLE.TEST",
                EmailConfirmed = true,
                SecurityStamp = "security-stamp",
                ConcurrencyStamp = "concurrency-stamp"
            });
            await legacy.SaveChangesAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO StrengthPrograms (Id, UserId, Name, Version, CreatedAt)
                VALUES ({programId}, {userId}, {"Gammelt program"}, {Guid.NewGuid()}, {DateTime.UtcNow});
                INSERT INTO ProgramExercises (Id, ProgramId, Name, Weight, Sets, Repetitions, Note, Position)
                VALUES ({Guid.NewGuid()}, {programId}, {"Squat"}, {80m}, {3}, {10}, {"Roligt tempo"}, {0}),
                       ({Guid.NewGuid()}, {programId}, {"Lunges"}, {20m}, {3}, {12}, {null}, {0});
                """);
        }

        await using var upgraded = new FitnessDbContext(options);
        await upgraded.Database.MigrateAsync();
        var migrated = await upgraded.StrengthPrograms
            .Include(program => program.Workouts).ThenInclude(workout => workout.Exercises)
            .SingleAsync(program => program.Id == programId);
        var workout = Assert.Single(migrated.Workouts);
        Assert.Equal("Træning 1", workout.Name);
        Assert.Equal(["Lunges", "Squat"], workout.Exercises.Select(exercise => exercise.Name).Order());
        Assert.Equal([0, 1], workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise => exercise.Position));
        Assert.Equal("Roligt tempo", workout.Exercises.Single(exercise => exercise.Name == "Squat").Note);
        Assert.All(workout.Exercises, exercise =>
        {
            Assert.Null(exercise.CatalogExerciseId);
            Assert.Null(exercise.DurationSeconds);
        });
        Assert.Equal(80m, workout.Exercises.Single(exercise => exercise.Name == "Squat").Weight);
        Assert.Equal(12, await upgraded.StrengthCatalog.CountAsync());
    }

    [Fact]
    public async Task CatalogIdentityAndDurationSurviveTemplateChangesAndCompletionRetries()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var owner = await Login(factory);
        using var other = await Login(factory);

        var catalog = await owner.GetFromJsonAsync<CatalogExerciseResponse[]>("/api/strength/exercises");
        Assert.NotNull(catalog);
        Assert.Equal(12, catalog.Select(exercise => exercise.Id).Distinct().Count());
        var plank = Assert.Single(catalog, exercise => exercise.Id == "plank");
        Assert.Equal("Plank", plank.Name);
        Assert.Contains("planke", plank.SearchAliases);
        Assert.True(plank.IsTimed);

        var draft = new SaveProgramRequest
        {
            Name = "Core",
            Workouts =
            [
                new WorkoutRequest
                {
                    Name = "Core",
                    Exercises =
                    [
                        new ExerciseRequest { CatalogExerciseId = "plank", Name = "Et andet navn", DurationSeconds = 75, Repetitions = 1 },
                        new ExerciseRequest { CatalogExerciseId = "squat", Name = "Knæbøjning", Weight = 20 }
                    ]
                }
            ]
        };
        var input = draft.Workouts[0].Exercises![0];
        input.CatalogExerciseId = "unknown-exercise";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Programs, draft)).StatusCode);
        input.CatalogExerciseId = "plank";
        input.Weight = 20;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Programs, draft)).StatusCode);
        input.Weight = 0;
        var created = await CreateCatalogProgram(draft);
        var secondProgram = await CreateCatalogProgram(draft);
        var workout = Assert.Single(created.Workouts);
        var timed = workout.Exercises[0];
        Assert.Equal("plank", timed.CatalogExerciseId);
        Assert.Equal("Plank", timed.Name);
        Assert.Equal(75, timed.DurationSeconds);
        Assert.Equal("squat", workout.Exercises[1].CatalogExerciseId);
        Assert.Null(workout.Exercises[1].DurationSeconds);
        Assert.Equal(20m, workout.Exercises[1].Weight);
        Assert.Equal(timed.CatalogExerciseId, secondProgram.Workouts[0].Exercises[0].CatalogExerciseId);
        Assert.NotEqual(timed.Id, secondProgram.Workouts[0].Exercises[0].Id);

        var completion = Completion(workout);
        var completionPath = $"{Programs}/{created.Id}/workouts/{workout.Id}/complete";
        completion.Exercises![0].DurationSeconds = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(completionPath, completion)).StatusCode);
        completion.Exercises[0].DurationSeconds = 86;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(completionPath, completion)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(completionPath, completion)).StatusCode);
        completion.Exercises[0].DurationSeconds = 87;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(completionPath, completion)).StatusCode);

        var updated = Draft(created);
        updated.Workouts![0].Exercises![0].DurationSeconds = 90;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"{Programs}/{created.Id}", updated)).StatusCode);
        var active = await owner.GetFromJsonAsync<PlannedWorkoutResponse>($"{Programs}/{created.Id}/workouts/{workout.Id}");
        Assert.Equal(90, active!.Exercises[0].DurationSeconds);
        Assert.Equal(86, active.Exercises[0].PreviousDurationSeconds);

        var current = (await owner.GetFromJsonAsync<ProgramResponse>($"{Programs}/{created.Id}"))!;
        var oldClientDraft = Draft(current);
        oldClientDraft.Workouts![0].Exercises![0].CatalogExerciseId = null;
        oldClientDraft.Workouts[0].Exercises![0].DurationSeconds = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"{Programs}/{created.Id}", oldClientDraft)).StatusCode);

        var replacement = Draft(current);
        replacement.Workouts![0].Exercises![0].CatalogExerciseId = "side-plank";
        var replacementResponse = await owner.PutAsJsonAsync($"{Programs}/{created.Id}", replacement);
        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        var replaced = (await replacementResponse.Content.ReadFromJsonAsync<ProgramResponse>())!.Workouts[0].Exercises[0];
        Assert.Equal("side-plank", replaced.CatalogExerciseId);
        Assert.NotEqual(timed.Id, replaced.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(completionPath, Completion(workout))).StatusCode);
        completion.Exercises[0].DurationSeconds = 86;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(completionPath, completion)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        var completedId = await db.CompletedWorkouts.Select(item => item.Id).SingleAsync();
        var historyPath = $"/api/strength/completions/{completedId}";
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(historyPath)).StatusCode);
        var history = await owner.GetFromJsonAsync<CompletedWorkoutResponse>(historyPath);
        Assert.Equal("plank", history!.Exercises[0].CatalogExerciseId);
        Assert.Equal(86, history.Exercises[0].DurationSeconds);

        async Task<ProgramResponse> CreateCatalogProgram(SaveProgramRequest request)
        {
            var response = await owner.PostAsJsonAsync(Programs, request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProgramResponse>())!;
        }
    }

    [Fact]
    public async Task CatalogHistoryUsesOnlyTheOwnersLatestCompletedMeasurementAcrossPrograms()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var owner = await Login(factory);
        using var other = await Login(factory);
        var draft = new SaveProgramRequest
        {
            Name = "Styrkepas",
            Workouts =
            [
                new WorkoutRequest
                {
                    Name = "Pas",
                    Exercises =
                    [
                        new ExerciseRequest { CatalogExerciseId = "squat", Weight = 35 },
                        new ExerciseRequest { CatalogExerciseId = "plank", DurationSeconds = 45, Repetitions = 1 },
                        new ExerciseRequest { Name = "Squat", Weight = 10 }
                    ]
                }
            ]
        };
        var first = await Create(owner, draft);
        var second = await Create(owner, draft);
        var foreign = await Create(other, draft);
        var secondWorkout = Assert.Single(second.Workouts);
        var activePath = $"{Programs}/{second.Id}/workouts/{secondWorkout.Id}";

        await Complete(owner, first, 17.5m, 60);
        await Complete(owner, first, 25.5m, 86);
        await Complete(owner, first, 50m, 100, isCompleted: false);
        await Complete(other, foreign, 90m, 150);

        var active = await owner.GetFromJsonAsync<PlannedWorkoutResponse>(activePath);
        Assert.NotNull(active);
        var squat = Assert.Single(active.Exercises, exercise => exercise.CatalogExerciseId == "squat");
        Assert.Equal(35m, squat.Weight);
        Assert.Equal(25.5m, squat.PreviousWeight);
        Assert.Equal(2, squat.PreviousSets);
        Assert.Equal(15, squat.PreviousRepetitions);
        Assert.Null(squat.PreviousDurationSeconds);
        var plank = Assert.Single(active.Exercises, exercise => exercise.CatalogExerciseId == "plank");
        Assert.Equal(86, plank.PreviousDurationSeconds);
        Assert.Equal(2, plank.PreviousSets);
        Assert.Null(plank.PreviousWeight);
        Assert.Null(plank.PreviousRepetitions);
        Assert.Null(Assert.Single(active.Exercises, exercise => exercise.CatalogExerciseId is null).PreviousWeight);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(activePath)).StatusCode);

        async Task Complete(HttpClient client, ProgramResponse program, decimal weight, int duration, bool isCompleted = true)
        {
            var workout = program.Workouts[0];
            var completion = Completion(workout);
            var exercises = completion.Exercises!;
            exercises[0].Weight = weight;
            exercises[0].Sets = 2;
            exercises[0].Repetitions = 15;
            exercises[0].IsCompleted = isCompleted;
            exercises[1].DurationSeconds = duration;
            exercises[1].Sets = 2;
            exercises[1].IsCompleted = isCompleted;
            exercises[2].IsCompleted = true;
            var response = await client.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{workout.Id}/complete", completion);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProgressionUsesOwnerSnapshotsAndCopenhagenDaysAcrossPrograms()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var owner = await Login(factory);
        using var other = await Login(factory);
        var draft = new SaveProgramRequest
        {
            Name = "Progression",
            Workouts =
            [
                new WorkoutRequest
                {
                    Name = "Styrkepas",
                    Exercises =
                    [
                        new ExerciseRequest { CatalogExerciseId = "squat", Weight = 30 },
                        new ExerciseRequest { CatalogExerciseId = "plank", DurationSeconds = 45, Repetitions = 1 },
                        new ExerciseRequest { Name = "Squat", Weight = 10 }
                    ]
                }
            ]
        };
        var first = await Create(owner, draft);
        var second = await Create(owner, draft);
        var foreign = await Create(other, draft);
        const string path = "/api/strength/progression?days=";
        Assert.Empty((await owner.GetFromJsonAsync<StrengthExerciseProgressionResponse[]>(path + 30))!);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var firstDay = today.AddDays(-29);
        var boundary = TimeZoneInfo.ConvertTimeToUtc(firstDay.ToDateTime(TimeOnly.MinValue), zone);
        await Complete(owner, first, 22.5m, 8, 62, boundary);
        await Complete(owner, second, 30m, 6, 75, boundary.AddHours(2));
        await Complete(owner, second, 35m, 7, 82, boundary.AddDays(3));
        await Complete(owner, first, 42m, 9, 90, boundary.AddSeconds(-1));
        await Complete(owner, first, 125m, 21, 300, boundary.AddHours(3), performed: false);
        await Complete(other, foreign, 300m, 22, 240, boundary.AddHours(3));
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"{Programs}/{first.Id}?version={first.Version}")).StatusCode);

        var recent = (await owner.GetFromJsonAsync<StrengthExerciseProgressionResponse[]>(path + 30))!;
        var squat = Assert.Single(recent, exercise => exercise.Key == "catalog:squat:weight");
        Assert.Equal(new StrengthProgressionDayResponse(firstDay, 30m, 8, 720m, null), squat.Days[0]);
        Assert.Equal(new StrengthProgressionDayResponse(firstDay.AddDays(3), 35m, 7, 490m, null), squat.Days[1]);
        Assert.Equal(2, squat.Days.Count);
        var plank = Assert.Single(recent, exercise => exercise.IsTimed);
        Assert.Equal(new StrengthProgressionDayResponse(firstDay, null, null, null, 75), plank.Days[0]);
        Assert.Equal(new StrengthProgressionDayResponse(firstDay.AddDays(3), null, null, null, 82), plank.Days[1]);
        var manual = recent.Where(exercise => exercise.Key.StartsWith("manual:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, manual.Length);
        Assert.Equal([1, 2], manual.Select(exercise => exercise.Days.Count).Order());

        var earlier = (await owner.GetFromJsonAsync<StrengthExerciseProgressionResponse[]>(path + 90))!
            .Single(exercise => exercise.Key == squat.Key);
        Assert.Equal(new StrengthProgressionDayResponse(firstDay.AddDays(-1), 42m, 9, 756m, null), earlier.Days[0]);
        Assert.Equal(3, earlier.Days.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + 366)).StatusCode);

        async Task Complete(HttpClient client, ProgramResponse program, decimal weight, int repetitions, int duration,
            DateTime completedAt, bool performed = true)
        {
            var workout = program.Workouts[0];
            var input = Completion(workout);
            var exercises = input.Exercises!;
            exercises[0].Weight = weight;
            exercises[0].Sets = 2;
            exercises[0].Repetitions = repetitions;
            exercises[0].IsCompleted = performed;
            exercises[1].DurationSeconds = duration;
            exercises[1].IsCompleted = performed;
            exercises[2].IsCompleted = performed;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
                $"{Programs}/{program.Id}/workouts/{workout.Id}/complete", input)).StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            var saved = await db.CompletedWorkouts.SingleAsync(item => item.CompletionId == input.CompletionId);
            saved.CompletedAt = completedAt;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ProgramPersistsMultipleOrderedWorkoutsWithExerciseNotes()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);

        var created = await Create(client);
        Assert.Equal("4 day split", created.Name);
        Assert.Equal(["Ben", "Ryg"], created.Workouts.Select(workout => workout.Name));
        Assert.Equal(["Squat", "Lunges"], created.Workouts[0].Exercises.Select(exercise => exercise.Name));
        Assert.Equal("Kontrolleret tempo", created.Workouts[0].Exercises[0].Note);

        var draft = Draft(created);
        draft.Workouts!.Reverse();
        draft.Workouts[1].Exercises!.Reverse();
        draft.Workouts[0].Exercises!.Add(new ExerciseRequest { Name = "Cable row", Weight = 45, Sets = 3, Repetitions = 12, Note = "Langsomt træk" });
        var response = await client.PutAsJsonAsync($"{Programs}/{created.Id}", draft);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ProgramResponse>())!;
        Assert.Equal(["Ryg", "Ben"], updated.Workouts.Select(workout => workout.Name));
        Assert.Equal(["Lunges", "Squat"], updated.Workouts[1].Exercises.Select(exercise => exercise.Name));
        Assert.Equal("Langsomt træk", updated.Workouts[0].Exercises[2].Note);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        Assert.Equal(2, await db.ProgramWorkouts.CountAsync());
        Assert.Equal(5, await db.ProgramExercises.CountAsync());
    }

    [Fact]
    public async Task ScheduleAndCompletedWorkoutAreOwnedAndPersisted()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var owner = await Login(factory);
        using var other = await Login(factory);
        var program = await Create(owner);
        var legs = program.Workouts[0];

        var schedule = Week();
        schedule[0] = new ScheduleEntryRequest(DayOfWeek.Monday, legs.Id);
        var invalidDays = Week();
        invalidDays[0] = new ScheduleEntryRequest((DayOfWeek)7, legs.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"{Programs}/{program.Id}/schedule", new ScheduleResponse(program.Version, invalidDays))).StatusCode);
        var nullEntry = Week();
        nullEntry[1] = null!;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"{Programs}/{program.Id}/schedule", new ScheduleResponse(program.Version, nullEntry))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{Programs}/{program.Id}/schedule", new ScheduleResponse(program.Version, schedule))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Programs}/{program.Id}/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", Completion(legs))).StatusCode);

        var completion = Completion(legs);
        completion.Exercises![0].Name = "Forkert navn fra klienten";
        var complete = await owner.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", completion);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", completion)).StatusCode);
        var conflictingRetry = Completion(legs);
        conflictingRetry.CompletionId = completion.CompletionId;
        conflictingRetry.Exercises![0].Weight += 2.5m;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", conflictingRetry)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().CompletedWorkouts.Include(item => item.Exercises).SingleAsync();
        Assert.Equal(legs.Name, history.WorkoutName);
        Assert.Equal(legs.Exercises.Count, history.Exercises.Count);
        Assert.Contains(history.Exercises, exercise => exercise.Name == "Squat" && exercise.IsCompleted);
        Assert.DoesNotContain(history.Exercises, exercise => exercise.Name == "Forkert navn fra klienten");
        var active = (await owner.GetFromJsonAsync<PlannedWorkoutResponse>($"{Programs}/{program.Id}/workouts/{legs.Id}"))!;
        Assert.Equal(legs.Exercises[0].Weight, active.Exercises[0].PreviousWeight);
        Assert.Equal(legs.Exercises[0].Sets, active.Exercises[0].PreviousSets);

        var skipped = Completion(legs);
        skipped.Exercises![0].IsCompleted = false;
        skipped.Exercises[0].Weight += 2.5m;
        skipped.Exercises[1].IsCompleted = true;
        skipped.Exercises[1].Weight += 2.5m;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", skipped)).StatusCode);
        var afterSkipped = (await owner.GetFromJsonAsync<PlannedWorkoutResponse>($"{Programs}/{program.Id}/workouts/{legs.Id}"))!;
        Assert.Equal(legs.Exercises[0].Weight, afterSkipped.Exercises[0].PreviousWeight);
        Assert.Equal(legs.Exercises[1].Weight + 2.5m, afterSkipped.Exercises[1].PreviousWeight);
    }

    [Theory]
    [InlineData("empty-program")]
    [InlineData("empty-workout")]
    [InlineData("bad-exercise")]
    [InlineData("long-note")]
    [InlineData("too-many-workouts")]
    public async Task InvalidAggregateIsRejectedWithoutChangingTheProgram(string scenario)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var original = await Create(client);
        var draft = Draft(original);
        switch (scenario)
        {
            case "empty-program": draft.Name = " "; break;
            case "empty-workout": draft.Workouts![0].Name = " "; break;
            case "bad-exercise": draft.Workouts![0].Exercises![0].Sets = 0; break;
            case "long-note": draft.Workouts![0].Exercises![0].Note = new string('x', 251); break;
            case "too-many-workouts": draft.Workouts = Enumerable.Range(0, 13).Select(index => new WorkoutRequest { Name = $"Træning {index}", Exercises = [] }).ToList(); break;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Programs}/{original.Id}", draft)).StatusCode);
        var after = (await client.GetFromJsonAsync<ProgramResponse>($"{Programs}/{original.Id}"))!;
        Assert.Equal(original.Version, after.Version);
        Assert.Equal(original.Workouts.Select(item => item.Id), after.Workouts.Select(item => item.Id));
    }

    [Fact]
    public async Task ForeignAndDuplicateNestedIdsAndStaleWritesAreRejected()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var program = await Create(client);
        var duplicate = Draft(program);
        duplicate.Workouts!.Add(Copy(program.Workouts[0]));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Programs}/{program.Id}", duplicate)).StatusCode);
        var foreign = Draft(program);
        foreign.Workouts![0].Exercises!.Add(new ExerciseRequest { Id = Guid.NewGuid(), Name = "Fremmed", Weight = 1, Sets = 1, Repetitions = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Programs}/{program.Id}", foreign)).StatusCode);
        var update = Draft(program); update.Name = "Opdateret";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Programs}/{program.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Programs}/{program.Id}", Draft(program))).StatusCode);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("revoked")]
    public async Task AllStrengthEndpointsRequireAnApprovedLiveSession(string state)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var login = (await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword })).Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var program = await Create(client);
        if (state == "anonymous") client.DefaultRequestHeaders.Authorization = null;
        if (state == "pending") await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Pending);
        if (state == "rejected") await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Rejected);
        if (state == "revoked") await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/strength/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/strength/exercises")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/strength/progression?days=90")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Programs}/{program.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"{Programs}/{program.Id}/schedule", new ScheduleResponse(program.Version, Week()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{program.Workouts[0].Id}/complete", Completion(program.Workouts[0]))).StatusCode);
    }

    private static async Task<HttpClient> Login(AuthWebApplicationFactory factory, string role = AuthenticationConstants.UserRole)
    {
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved, role);
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword });
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<ProgramResponse> Create(HttpClient client, SaveProgramRequest? draft = null)
    {
        var response = await client.PostAsJsonAsync(Programs, draft ?? Valid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>())!;
    }

    private static SaveProgramRequest Valid() => new()
    {
        Name = "  4 day split  ",
        Workouts =
        [
            new WorkoutRequest { Name = "Ben", Exercises = [new ExerciseRequest { Name = "Squat", Weight = 82.5m, Sets = 4, Repetitions = 8, Note = "Kontrolleret tempo" }, new ExerciseRequest { Name = "Lunges", Weight = 20m, Sets = 3, Repetitions = 10 }] },
            new WorkoutRequest { Name = "Ryg", Exercises = [new ExerciseRequest { Name = "Dødløft", Weight = 100m, Sets = 4, Repetitions = 6 }, new ExerciseRequest { Name = "Bent over row", Weight = 50m, Sets = 3, Repetitions = 10 }] }
        ]
    };

    private static SaveProgramRequest Draft(ProgramResponse program) => new() { Name = program.Name, Version = program.Version, Workouts = program.Workouts.Select(Copy).ToList() };
    private static WorkoutRequest Copy(WorkoutResponse source) => new() { Id = source.Id, Name = source.Name, Exercises = source.Exercises.Select(item => new ExerciseRequest { Id = item.Id, Name = item.Name, Weight = item.Weight, Sets = item.Sets, Repetitions = item.Repetitions, Note = item.Note, CatalogExerciseId = item.CatalogExerciseId, DurationSeconds = item.DurationSeconds }).ToList() };
    private static List<ScheduleEntryRequest> Week() => [new(DayOfWeek.Monday, null), new(DayOfWeek.Tuesday, null), new(DayOfWeek.Wednesday, null), new(DayOfWeek.Thursday, null), new(DayOfWeek.Friday, null), new(DayOfWeek.Saturday, null), new(DayOfWeek.Sunday, null)];
    private static CompleteWorkoutRequest Completion(WorkoutResponse workout) => new() { CompletionId = Guid.NewGuid(), Exercises = workout.Exercises.Select((exercise, index) => new CompletedExerciseRequest { ProgramExerciseId = exercise.Id, Name = exercise.Name, Weight = exercise.Weight, Sets = exercise.Sets, Repetitions = exercise.Repetitions, IsCompleted = index == 0, DurationSeconds = exercise.DurationSeconds }).ToList() };
}
