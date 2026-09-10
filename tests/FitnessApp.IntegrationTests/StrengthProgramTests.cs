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
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var programId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            await db.GetService<IMigrator>().MigrateAsync(FlatExerciseMigration);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO StrengthPrograms (Id, UserId, Name, Version, CreatedAt)
                VALUES ({programId}, {user.Id}, {"Gammelt program"}, {Guid.NewGuid()}, {DateTime.UtcNow});
                INSERT INTO ProgramExercises (Id, ProgramId, Name, Weight, Sets, Repetitions, Note, Position)
                VALUES ({Guid.NewGuid()}, {programId}, {"Squat"}, {80m}, {3}, {10}, {"Roligt tempo"}, {0}),
                       ({Guid.NewGuid()}, {programId}, {"Lunges"}, {20m}, {3}, {12}, {null}, {0});
                """);
            await db.Database.MigrateAsync();
        }

        using var verify = factory.Services.CreateScope();
        var migrated = await verify.ServiceProvider.GetRequiredService<FitnessDbContext>().StrengthPrograms
            .Include(program => program.Workouts).ThenInclude(workout => workout.Exercises).SingleAsync(program => program.Id == programId);
        var workout = Assert.Single(migrated.Workouts);
        Assert.Equal("Træning 1", workout.Name);
        Assert.Equal(["Lunges", "Squat"], workout.Exercises.Select(exercise => exercise.Name).Order());
        Assert.Equal([0, 1], workout.Exercises.OrderBy(exercise => exercise.Position).Select(exercise => exercise.Position));
        Assert.Equal("Roligt tempo", workout.Exercises.Single(exercise => exercise.Name == "Squat").Note);
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
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"{Programs}/{program.Id}/workouts/{legs.Id}/complete", skipped)).StatusCode);
        var afterSkipped = (await owner.GetFromJsonAsync<PlannedWorkoutResponse>($"{Programs}/{program.Id}/workouts/{legs.Id}"))!;
        Assert.Equal(legs.Exercises[0].Weight, afterSkipped.Exercises[0].PreviousWeight);
        Assert.Equal(legs.Exercises[1].Weight, afterSkipped.Exercises[1].PreviousWeight);
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

    private static async Task<ProgramResponse> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Programs, Valid());
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
    private static WorkoutRequest Copy(WorkoutResponse source) => new() { Id = source.Id, Name = source.Name, Exercises = source.Exercises.Select(item => new ExerciseRequest { Id = item.Id, Name = item.Name, Weight = item.Weight, Sets = item.Sets, Repetitions = item.Repetitions, Note = item.Note }).ToList() };
    private static List<ScheduleEntryRequest> Week() => [new(DayOfWeek.Monday, null), new(DayOfWeek.Tuesday, null), new(DayOfWeek.Wednesday, null), new(DayOfWeek.Thursday, null), new(DayOfWeek.Friday, null), new(DayOfWeek.Saturday, null), new(DayOfWeek.Sunday, null)];
    private static CompleteWorkoutRequest Completion(WorkoutResponse workout) => new() { CompletionId = Guid.NewGuid(), Exercises = workout.Exercises.Select((exercise, index) => new CompletedExerciseRequest { ProgramExerciseId = exercise.Id, Name = exercise.Name, Weight = exercise.Weight, Sets = exercise.Sets, Repetitions = exercise.Repetitions, IsCompleted = index == 0 }).ToList() };
}
