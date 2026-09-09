using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Strength;
using FitnessApp.Contracts.Strength;

namespace FitnessApp.Server.Strength;

internal static class StrengthProgramEndpoints
{
    public static void MapStrengthProgramEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The existing JWT validator checks the persisted session and live Approved status on every request.
        var group = endpoints.MapGroup("/api/strength/programs").RequireAuthorization();
        group.MapGet("/", async (ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(Owner(user), ct)).Select(Map)));
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            await service.GetAsync(Owner(user), id, ct) is { } program ? Results.Ok(Map(program)) : Missing());
        group.MapPost("/", (SaveProgramRequest request, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Save(null, request, user, service, ct));
        group.MapPut("/{id:guid}", (Guid id, SaveProgramRequest request, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Save(id, request, user, service, ct));
        group.MapDelete("/{id:guid}", async (Guid id, Guid version, ClaimsPrincipal user, IStrengthProgramService service, CancellationToken ct) =>
            Status(await service.DeleteAsync(Owner(user), id, version, ct)));
    }

    private static async Task<IResult> Save(Guid? id, SaveProgramRequest request, ClaimsPrincipal user,
        IStrengthProgramService service, CancellationToken ct)
    {
        var result = await service.SaveAsync(Owner(user), id, new ProgramInput(request.Name, request.Version,
            request.Exercises?.Select(e => e is null ? null! : new ExerciseInput(e.Id, e.Name, e.Sets, e.Repetitions, e.IsWarmUp)).ToArray()), ct);
        return result.Status == ProgramStatus.Saved
            ? id is null ? Results.Created($"/api/strength/programs/{result.Program!.Id}", Map(result.Program)) : Results.Ok(Map(result.Program!))
            : Status(result.Status);
    }

    private static IResult Status(ProgramStatus status) => status switch
    {
        ProgramStatus.Saved => Results.NoContent(),
        ProgramStatus.NotFound => Missing(),
        ProgramStatus.Conflict => Results.Conflict(new { title = "Programmet er ændret i en anden fane. Genindlæs for at se den nyeste version." }),
        _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["Program"] =
            ["Angiv et navn på 1–100 tegn og 1–50 øvelser. Hver øvelse skal have et navn på 1–100 tegn, 1–10 sæt og 1–30 gentagelser. Øvelser skal tilhøre programmet."] })
    };
    private static IResult Missing() => Results.NotFound(new { title = "Programmet blev ikke fundet." });
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
    private static ProgramResponse Map(ProgramData program) => new(program.Id, program.Name, program.Version,
        program.Exercises.Select(e => new ExerciseResponse(e.Id, e.Name, e.Sets, e.Repetitions, e.IsWarmUp)).ToArray());
}
