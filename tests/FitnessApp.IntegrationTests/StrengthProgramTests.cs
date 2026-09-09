using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Contracts.Strength;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.IntegrationTests;

public sealed class StrengthProgramTests
{
    private const string Url = "/api/strength/programs";

    [Fact]
    public async Task CompleteLifecyclePersistsAcrossContextsAndDeletesChildren()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var created = await Create(client);
        Assert.Equal("Ben æøå", created.Name);
        Assert.Equal(new[] { "Squat", "Lunges" }, created.Exercises.Select(e => e.Name));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            var persisted = await db.StrengthPrograms.Include(p => p.Exercises).SingleAsync();
            Assert.Equal(created.Id, persisted.Id);
            Assert.Equal(2, persisted.Exercises.Count);
        }
        var draft = Draft(created);
        draft.Name = "  Bryst og arme  ";
        draft.Exercises = [draft.Exercises![1], new() { Name = "Bænkpres", Sets = 10, Repetitions = 30, IsWarmUp = true }];
        draft.Exercises[0].Name = "  Udfald  ";
        draft.Exercises[0].Sets = 1;
        draft.Exercises[0].Repetitions = 1;
        var update = await client.PutAsJsonAsync($"{Url}/{created.Id}", draft);
        Assert.True(update.StatusCode == HttpStatusCode.OK,
            $"Expected OK but received {update.StatusCode}: {await update.Content.ReadAsStringAsync()}");
        var saved = (await update.Content.ReadFromJsonAsync<ProgramResponse>())!;
        Assert.NotEqual(created.Version, saved.Version);
        var reloaded = (await client.GetFromJsonAsync<ProgramResponse>($"{Url}/{created.Id}"))!;
        Assert.Equal("Bryst og arme", reloaded.Name);
        Assert.Equal(new[] { "Udfald", "Bænkpres" }, reloaded.Exercises.Select(e => e.Name));
        Assert.Equal(created.Exercises[1].Id, reloaded.Exercises[0].Id);
        Assert.Equal(1, reloaded.Exercises[0].Sets);
        Assert.Equal(30, reloaded.Exercises[1].Repetitions);
        Assert.True(reloaded.Exercises[1].IsWarmUp);
        Assert.False(reloaded.Exercises[0].IsWarmUp);
        using var secondFactory = new AuthWebApplicationFactory(storage: factory.Storage);
        using var secondClient = secondFactory.CreateHttpsClient();
        using (var scope = secondFactory.Services.CreateScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().ProgramExercises.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{Url}/{created.Id}?version={created.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{created.Id}?version={saved.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Url}/{created.Id}?version={saved.Version}")).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<FitnessDbContext>();
        Assert.Empty(await finalDb.StrengthPrograms.ToListAsync());
        Assert.Empty(await finalDb.ProgramExercises.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherUserAndAdminCannotAccessOwnedProgram(bool admin)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var a = await Login(factory);
        using var b = await Login(factory, admin ? AuthenticationConstants.AdminRole : AuthenticationConstants.UserRole);
        var owned = await Create(a);
        var other = await Create(b);
        Assert.Equal(new[] { owned.Id }, (await a.GetFromJsonAsync<List<ProgramResponse>>(Url))!.Select(p => p.Id));
        Assert.Equal(new[] { other.Id }, (await b.GetFromJsonAsync<List<ProgramResponse>>(Url))!.Select(p => p.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"{Url}/{owned.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"{Url}/{owned.Id}", Draft(owned))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"{Url}/{owned.Id}?version={owned.Version}")).StatusCode);
        var spoof = await b.PostAsJsonAsync(Url, new { Name = "Spoof", UserId = "someone-else", Exercises = new[] { new { Name = "Squat", Sets = 3, Repetitions = 10 } } });
        Assert.Equal(HttpStatusCode.Created, spoof.StatusCode);
        Assert.Single((await a.GetFromJsonAsync<List<ProgramResponse>>(Url))!);
        Assert.Equal(2, (await b.GetFromJsonAsync<List<ProgramResponse>>(Url))!.Count);
    }

    [Fact]
    public async Task ForeignAndDuplicateExerciseIdsAreRejectedAtomically()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var first = await Create(client);
        var second = await Create(client);
        var draft = Draft(first);
        draft.Name = "Must not persist";
        draft.Exercises![0].Id = second.Exercises[0].Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{first.Id}", draft)).StatusCode);
        draft.Exercises[0].Id = first.Exercises[1].Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{first.Id}", draft)).StatusCode);
        var reloaded = (await client.GetFromJsonAsync<ProgramResponse>($"{Url}/{first.Id}"))!;
        Assert.Equal(first.Name, reloaded.Name);
        Assert.Equal(first.Version, reloaded.Version);
        Assert.Equal(first.Exercises.ToArray(), reloaded.Exercises.ToArray());
        Assert.Equal(second.Exercises.ToArray(), (await client.GetFromJsonAsync<ProgramResponse>($"{Url}/{second.Id}"))!.Exercises.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Draft(first))).StatusCode);
    }

    [Theory]
    [InlineData("empty-name")]
    [InlineData("long-name")]
    [InlineData("empty-exercise")]
    [InlineData("long-exercise")]
    [InlineData("zero-sets")]
    [InlineData("many-sets")]
    [InlineData("zero-reps")]
    [InlineData("many-reps")]
    [InlineData("empty-list")]
    [InlineData("long-list")]
    [InlineData("null-list")]
    [InlineData("null-exercise")]
    public async Task InvalidCreateAndUpdateLeaveDatabaseUnchanged(string scenario)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var original = await Create(client);
        var draft = Draft(original);
        switch (scenario)
        {
            case "empty-name": draft.Name = " \t "; break;
            case "long-name": draft.Name = new string('æ', 101); break;
            case "empty-exercise": draft.Exercises![0].Name = " "; break;
            case "long-exercise": draft.Exercises![0].Name = new string('ø', 101); break;
            case "zero-sets": draft.Exercises![0].Sets = 0; break;
            case "many-sets": draft.Exercises![0].Sets = 11; break;
            case "zero-reps": draft.Exercises![0].Repetitions = 0; break;
            case "many-reps": draft.Exercises![0].Repetitions = 31; break;
            case "empty-list": draft.Exercises = []; break;
            case "long-list": draft.Exercises = Enumerable.Range(0, 51).Select(_ => new ExerciseRequest { Name = "Squat" }).ToList(); break;
            case "null-list": draft.Exercises = null; break;
            case "null-exercise": draft.Exercises = [null!]; break;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Url}/{original.Id}", draft)).StatusCode);
        if (draft.Exercises is not null) foreach (var exercise in draft.Exercises) if (exercise is not null) exercise.Id = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, draft)).StatusCode);
        var reloaded = (await client.GetFromJsonAsync<ProgramResponse>($"{Url}/{original.Id}"))!;
        Assert.Equal(original.Name, reloaded.Name);
        Assert.Equal(original.Version, reloaded.Version);
        Assert.Equal(original.Exercises.ToArray(), reloaded.Exercises.ToArray());
        Assert.Single((await client.GetFromJsonAsync<List<ProgramResponse>>(Url))!);
    }

    [Fact]
    public async Task BoundsAndReorderAndStaleUpdates()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        using var client = await Login(factory);
        var original = await Create(client);
        var draft = Draft(original);
        draft.Exercises!.Reverse();
        var saved = await (await client.PutAsJsonAsync($"{Url}/{original.Id}", draft)).Content.ReadFromJsonAsync<ProgramResponse>();
        Assert.Equal(original.Exercises.Reverse().ToArray(), saved!.Exercises.ToArray());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Url}/{original.Id}", draft)).StatusCode);
        draft = new() { Name = new string('å', 100), Exercises = Enumerable.Range(0, 50).Select(_ => new ExerciseRequest { Name = new string('æ', 100), Sets = 10, Repetitions = 30 }).ToList() };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Url, draft)).StatusCode);
        var invalidNumber = new StringContent("{\"name\":\"Bad\",\"exercises\":[{\"name\":\"Squat\",\"sets\":1.5,\"repetitions\":10}]}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Url, invalidNumber)).StatusCode);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("revoked")]
    public async Task EveryEndpointUsesLiveSessionAndApprovalChecks(string scenario)
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        using var client = factory.CreateHttpsClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var program = await Create(client);
        if (scenario == "anonymous") client.DefaultRequestHeaders.Authorization = null;
        if (scenario == "pending") await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Pending);
        if (scenario == "rejected") await factory.SetApprovalStatusAsync(user.Id, AccountApprovalStatus.Rejected);
        if (scenario == "revoked") await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}/{program.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Url, new SaveProgramRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"{Url}/{program.Id}", Draft(program))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"{Url}/{program.Id}?version={program.Version}")).StatusCode);
    }

    [Fact]
    public async Task UpgradeFromPasswordResetSchemaPreservesAccountSessionAndResetMetadata()
    {
        using var factory = new AuthWebApplicationFactory();
        await factory.InitializeDatabaseAsync();
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved);
        var sessionId = await factory.CreateSessionAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
        var queuedAt = DateTime.UtcNow.AddMinutes(-10);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20260909044755_AddPasswordResetCooldown");
            (await db.Users.SingleAsync()).LastPasswordResetEmailQueuedAt = queuedAt;
            await db.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitnessDbContext>();
            await db.Database.MigrateAsync();
            var preserved = await db.Users.SingleAsync();
            Assert.Equal(user.PasswordHash, preserved.PasswordHash);
            Assert.Equal(user.SecurityStamp, preserved.SecurityStamp);
            Assert.Equal(queuedAt, preserved.LastPasswordResetEmailQueuedAt);
            Assert.Equal(AccountApprovalStatus.Approved, preserved.ApprovalStatus);
            Assert.Single(await db.UserRoles.ToListAsync());
            Assert.Equal(sessionId, (await db.UserSessions.SingleAsync()).Id);
            Assert.Empty(await db.StrengthPrograms.ToListAsync());
        }
    }

    private static async Task<HttpClient> Login(AuthWebApplicationFactory factory, string role = AuthenticationConstants.UserRole)
    {
        var user = await factory.CreateUserAsync(AccountApprovalStatus.Approved, role);
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = user.Email!, Password = factory.ValidPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
    private static async Task<ProgramResponse> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Url, new SaveProgramRequest { Name = "  Ben æøå  ", Exercises = [new() { Name = "Squat", IsWarmUp = true }, new() { Name = "Lunges" }] });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>())!;
    }
    private static SaveProgramRequest Draft(ProgramResponse program) => new() { Name = program.Name, Version = program.Version,
        Exercises = program.Exercises.Select(e => new ExerciseRequest { Id = e.Id, Name = e.Name, Sets = e.Sets, Repetitions = e.Repetitions, IsWarmUp = e.IsWarmUp }).ToList() };
}
