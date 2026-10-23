using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Diary;
using FitnessApp.Contracts.Diary;

namespace FitnessApp.Server.Diary;

internal static class DiaryEndpoints
{
    public static IEndpointRouteBuilder MapDiaryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/diary").RequireAuthorization().RequireRateLimiting("notifications");
        group.MapGet("/state", async (HttpContext context, IDiaryService service, CancellationToken ct) =>
        {
            var r = await service.StateAsync(Owner(context), Session(context), Grant(context), ct);
            return Reply(r, x => new DiaryStatusResponse(x.Initialized, x.Protected, x.Unlocked, x.Today, x.TimeZoneId)
            {
                MinimumNewEntryDate = x.MinimumNewEntryDate
            });
        });
        group.MapPost("/unlock", async (DiaryCodeRequest request, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.UnlockAsync(Owner(context), Session(context), request.Code, ct), x => new DiaryUnlockResponse(x)));
        group.MapPut("/protection", async (ConfigureDiaryRequest request, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.ConfigureAsync(Owner(context), Session(context), request.CurrentCode, request.NewCode, request.Confirmation, ct), x => new DiaryUnlockResponse(x)));
        group.MapPost("/recover", async (RecoverDiaryRequest request, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.RecoverAsync(Owner(context), request.AccountPassword, ct), x => x));
        group.MapPost("/activity", async (HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.ActivityAsync(Owner(context), Session(context), Grant(context), ct), x => x));
        group.MapPost("/lock", async (HttpContext context, IDiaryService service, CancellationToken ct) =>
        {
            await service.LockAsync(Owner(context), Session(context), Grant(context), ct);
            return Results.Ok(true);
        });
        group.MapGet("/dates/{year:int}/{month:int}", async (int year, int month, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.DatesAsync(Owner(context), Session(context), Grant(context), year, month, ct),
                x => x.Select(d => new DiaryDateResponse(d.Date, d.Version)).ToArray()));
        group.MapGet("/entries/{date}", async (DateOnly date, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.ReadAsync(Owner(context), Session(context), Grant(context), date, ct), Map));
        group.MapPut("/entries/{date}", async (DateOnly date, SaveDiaryRequest request, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.SaveAsync(Owner(context), Session(context), Grant(context), date, request.Text, request.Version, ct), Map));
        group.MapDelete("/entries/{date}/{version:guid}", async (DateOnly date, Guid version, HttpContext context, IDiaryService service, CancellationToken ct) =>
            Reply(await service.DeleteAsync(Owner(context), Session(context), Grant(context), date, version, ct), x => x));
        return endpoints;
    }
    private static DiaryEntryResponse Map(DiaryEntryData entry) => new(entry.Date, entry.Text, entry.Version);
    private static IResult Reply<T, TResponse>(DiaryResult<T> result, Func<T, TResponse> map) => result.Status switch
    {
        DiaryStatus.Success => Results.Ok(map(result.Value!)),
        DiaryStatus.Locked => Results.Json(new DiaryErrorResponse("Lås dagbogen op for at fortsætte."), statusCode: 423),
        DiaryStatus.Limited => Results.Json(new DiaryErrorResponse("For mange forsøg. Vent fem minutter, og prøv igen."), statusCode: 429),
        DiaryStatus.Conflict => Results.Conflict(new DiaryErrorResponse("Dagbogssiden er ændret i en anden fane. Genindlæs den gemte version, før du fortsætter.")),
        DiaryStatus.NotFound => Results.NotFound(new DiaryErrorResponse("Der er endnu ingen gemt side på denne dato.")),
        DiaryStatus.Unavailable => Results.Json(new DiaryErrorResponse("Siden kan ikke åbnes med serverens nuværende nøgler. Kontakt den driftsansvarlige."), statusCode: 503),
        _ => Results.BadRequest(new DiaryErrorResponse("Kontrollér oplysningerne. Dagbogskoder skal være ens og indeholde 1–128 tegn. Sider kan højst indeholde 12.000 tegn."))
    };
    private static string Owner(HttpContext context) => context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
    private static string Session(HttpContext context) => context.User.FindFirstValue(JwtRegisteredClaimNames.Sid)!;
    private static string? Grant(HttpContext context) => context.Request.Headers["X-Diary-Grant"].FirstOrDefault();
}
