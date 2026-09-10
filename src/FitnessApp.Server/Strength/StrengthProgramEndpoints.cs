using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Strength;
using FitnessApp.Contracts.Strength;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Server.Strength;

internal static class StrengthProgramEndpoints
{
    public static void MapStrengthProgramEndpoints(this IEndpointRouteBuilder app)
    {
        // The existing JWT validator checks the persisted session and live Approved status on every request.
        var group = app.MapGroup("/api/strength").RequireAuthorization();
        group.MapGet("/overview", async (ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Results.Ok(Map(await service.GetOverviewAsync(Owner(user), ct))));
        group.MapGet("/programs", async (ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(Owner(user), ct)).Select(Map).ToArray()));
        group.MapPost("/programs", (SaveProgramRequest request, ClaimsPrincipal user, IStrengthProgramService service,
            CancellationToken ct) => Save(null, request, user, service, ct));
        group.MapGet("/programs/{id:guid}", async Task<Results<Ok<ProgramResponse>, NotFound<object>>>
            (Guid id, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            (await service.GetAsync(Owner(user), id, ct)) is { } program ? TypedResults.Ok(Map(program)) : TypedResults.NotFound(Missing()));
        group.MapPut("/programs/{id:guid}", (Guid id, SaveProgramRequest request, ClaimsPrincipal user,
            IStrengthProgramService service, CancellationToken ct) => Save(id, request, user, service, ct));
        group.MapDelete("/programs/{id:guid}", async (Guid id, Guid version, ClaimsPrincipal user,
            IStrengthProgramService service, CancellationToken ct) => Status(await service.DeleteAsync(Owner(user), id, version, ct)));
        group.MapGet("/programs/{id:guid}/schedule", async Task<Results<Ok<ScheduleResponse>, NotFound<object>>>
            (Guid id, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            (await service.GetScheduleAsync(Owner(user), id, ct)) is { } schedule
                ? TypedResults.Ok(new ScheduleResponse(schedule.Version,
                    schedule.Entries.Select(entry => new ScheduleEntryRequest(entry.DayOfWeek, entry.WorkoutId)).ToArray()))
                : TypedResults.NotFound(Missing()));
        group.MapPut("/programs/{id:guid}/schedule", async (Guid id, ScheduleResponse request, ClaimsPrincipal user,
            IStrengthProgramService service, CancellationToken ct) => Status(await service.SaveScheduleAsync(Owner(user), id,
                request.Version, request.Entries?.Select(entry => entry is null ? null! : new ScheduleEntryData(entry.DayOfWeek,
                    entry.WorkoutId)).ToArray(), ct)));
        group.MapGet("/programs/{programId:guid}/workouts/{workoutId:guid}", async Task<Results<Ok<PlannedWorkoutResponse>, NotFound<object>>>
            (Guid programId, Guid workoutId, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            (await service.GetWorkoutAsync(Owner(user), programId, workoutId, ct)) is { } workout
                ? TypedResults.Ok(Map(workout)) : TypedResults.NotFound(Missing()));
        group.MapPost("/programs/{programId:guid}/workouts/{workoutId:guid}/complete", async (Guid programId, Guid workoutId,
            CompleteWorkoutRequest request, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Status(await service.CompleteWorkoutAsync(Owner(user), programId, workoutId,
                new CompletionInput(request.CompletionId, request.Exercises?.Select(exercise => exercise is null ? null! : new CompletedExerciseInput(
                    exercise.ProgramExerciseId, exercise.Name, exercise.Weight, exercise.Sets, exercise.Repetitions,
                    exercise.IsCompleted)).ToArray()), ct)));
    }

    private static async Task<IResult> Save(Guid? id, SaveProgramRequest request, ClaimsPrincipal user,
        IStrengthProgramService service, CancellationToken ct)
    {
        var result = await service.SaveAsync(Owner(user), id, new ProgramInput(request.Name, request.Version,
            request.Workouts?.Select(workout => workout is null ? null! : new WorkoutInput(workout.Id, workout.Name,
                workout.Exercises?.Select(exercise => exercise is null ? null! : new ExerciseInput(exercise.Id,
                    exercise.Name, exercise.Weight, exercise.Sets, exercise.Repetitions, exercise.Note)).ToArray())).ToArray()), ct);
        return result.Status == ProgramStatus.Saved
            ? id is null ? Results.Created($"/api/strength/programs/{result.Program!.Id}", Map(result.Program)) : Results.Ok(Map(result.Program!))
            : Status(result.Status);
    }

    private static IResult Status(ProgramStatus status) => status switch
    {
        ProgramStatus.Saved => Results.NoContent(),
        ProgramStatus.NotFound => Results.NotFound(Missing()),
        ProgramStatus.Conflict => Results.Conflict(new
        {
            title = "Programmet er ændret i en anden fane. Genindlæs for at se den nyeste version."
        }),
        _ => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Program"] = ["Angiv et programnavn, gyldige træninger og gyldige øvelser. Kontrollér navn, vægt, sæt og gentagelser."]
        })
    };

    private static object Missing() => new { title = "Programmet eller træningen blev ikke fundet." };

    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    private static StrengthOverviewResponse Map(StrengthOverviewData overview) => new(overview.Programs.Select(Map).ToArray(),
        overview.Today is null ? null : Map(overview.Today));

    private static ProgramResponse Map(ProgramData program) => new(program.Id, program.Name, program.Version,
        program.Workouts.Select(Map).ToArray());

    private static WorkoutResponse Map(WorkoutData workout) => new(workout.Id, workout.Name,
        workout.Exercises.Select(Map).ToArray());

    private static ExerciseResponse Map(ExerciseData exercise) => new(exercise.Id, exercise.Name, exercise.Weight,
        exercise.Sets, exercise.Repetitions, exercise.Note);

    private static PlannedWorkoutResponse Map(PlannedWorkoutData workout) => new(workout.ProgramId, workout.ProgramName,
        workout.WorkoutId, workout.WorkoutName, workout.Exercises.Select(Map).ToArray());

    private static ActiveExerciseResponse Map(ActiveExerciseData exercise) => new(exercise.Id, exercise.Name, exercise.Weight,
        exercise.Sets, exercise.Repetitions, exercise.Note, exercise.PreviousWeight, exercise.PreviousSets,
        exercise.PreviousRepetitions);
}
