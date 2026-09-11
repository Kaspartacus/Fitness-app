using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Running;
using FitnessApp.Contracts.Running;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Server.Running;

internal static class RunningEndpoints
{
    public static void MapRunningEndpoints(this IEndpointRouteBuilder app)
    {
        // Protected running data is always scoped to the JWT subject, never to a user id supplied by the client.
        var group = app.MapGroup("/api/running").RequireAuthorization();

        group.MapGet("/overview", GetOverviewAsync);
        group.MapGet("/plans/{planId:guid}", GetPlanAsync);
        group.MapPost("/plans", CreatePlanAsync);
        group.MapPut("/plans/{planId:guid}/schedule", UpdatePlanScheduleAsync);
        group.MapPut("/plans/{activePlanId:guid}", ReplacePlanAsync);
        group.MapGet("/sessions", ListSessionsAsync);
        group.MapGet("/sessions/{sessionId:guid}", GetSessionAsync);
        group.MapPost("/sessions/{sessionId:guid}/start", StartSessionAsync);
        group.MapPost("/sessions/{sessionId:guid}/cancel", CancelSessionAsync);
        group.MapPost("/sessions/{sessionId:guid}/complete", CompleteSessionAsync);
        group.MapPost("/results/manual", CreateManualResultAsync);
        group.MapGet("/results/{resultId:guid}", GetResultAsync);
        group.MapPut("/results/{resultId:guid}", UpdateResultAsync);
    }

    private static async Task<IResult> GetOverviewAsync(
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken) =>
        Results.Ok(Map(await service.GetOverviewAsync(Owner(user), cancellationToken)));

    private static async Task<IResult> GetPlanAsync(
        Guid planId,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken) =>
        (await service.GetPlanAsync(Owner(user), planId, cancellationToken)) is { } plan
            ? Results.Ok(Map(plan))
            : Results.NotFound(MissingPlan());

    private static async Task<IResult> CreatePlanAsync(
        RunningPlanRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Plan", "Angiv løbeplanens oplysninger, og prøv igen.");
        }

        var result = await service.CreatePlanAsync(Owner(user), ToInput(request), cancellationToken);
        if (result.Status is RunningStatus.Saved && result.Plan is { } plan)
        {
            return Results.Created($"/api/running/plans/{plan.Id}", Map(plan));
        }

        return PlanStatus(result.Status);
    }

    private static async Task<IResult> ReplacePlanAsync(
        Guid activePlanId,
        ReplaceRunningPlanRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Plan", "Angiv løbeplanens oplysninger, og prøv igen.");
        }

        var result = await service.ReplacePlanAsync(
            Owner(user),
            activePlanId,
            new RunningPlanReplacementInput(request.Version, request.ReplaceActivePlan, ToInput(request)),
            cancellationToken);
        return result.Status is RunningStatus.Saved && result.Plan is { } plan
            ? Results.Ok(Map(plan))
            : PlanStatus(result.Status);
    }

    private static async Task<IResult> UpdatePlanScheduleAsync(
        Guid planId,
        UpdateRunningPlanScheduleRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Plan", "Angiv planens løbedage, og prøv igen.");
        }

        var result = await service.UpdatePlanScheduleAsync(
            Owner(user),
            planId,
            new UpdateRunningPlanScheduleInput(request.Version, request.SelectedDays?.ToArray()),
            cancellationToken);
        return result.Status is RunningStatus.Saved && result.Plan is { } plan
            ? Results.Ok(Map(plan))
            : PlanStatus(result.Status);
    }

    private static async Task<IResult> ListSessionsAsync(
        string? from,
        string? to,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (!TryReadDateRange(from, to, out var startDate, out var endDate, out var errors))
        {
            return Results.ValidationProblem(errors, title: "Kontrollér datoperioden.");
        }

        var result = await service.ListSessionsAsync(Owner(user), startDate, endDate, cancellationToken);
        return result.Status is RunningStatus.Saved
            ? Results.Ok(result.Sessions.Select(Map).ToArray())
            : SessionStatus(result.Status);
    }

    private static async Task<IResult> GetSessionAsync(
        Guid sessionId,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken) =>
        (await service.GetSessionAsync(Owner(user), sessionId, cancellationToken)) is { } session
            ? Results.Ok(Map(session))
            : Results.NotFound(MissingSession());

    private static async Task<IResult> StartSessionAsync(
        Guid sessionId,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        var result = await service.StartSessionAsync(Owner(user), sessionId, cancellationToken);
        return result.Status is RunningStatus.Saved && result.Session is { } session
            ? Results.Ok(Map(session))
            : SessionStatus(result.Status, result.Session);
    }

    private static async Task<IResult> CancelSessionAsync(
        Guid sessionId,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CancelSessionAsync(Owner(user), sessionId, cancellationToken);
        return result.Status is RunningStatus.Saved && result.Session is { } session
            ? Results.Ok(Map(session))
            : SessionStatus(result.Status, result.Session);
    }

    private static async Task<IResult> CreateManualResultAsync(
        ManualRunningResultRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Resultat", "Angiv løbeturens oplysninger, og prøv igen.");
        }

        var result = await service.CreateManualResultAsync(Owner(user), ToInput(request), cancellationToken);
        return result.Status is RunningStatus.Saved && result.Result is { } runningResult
            ? Results.Created($"/api/running/results/{runningResult.Id}", Map(runningResult))
            : ResultStatus(result.Status, result.Result);
    }

    private static async Task<IResult> CompleteSessionAsync(
        Guid sessionId,
        CompleteRunningSessionRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Resultat", "Angiv løbeturens oplysninger, og prøv igen.");
        }

        var result = await service.CompleteSessionAsync(Owner(user), sessionId, ToInput(request), cancellationToken);
        return result.Status is RunningStatus.Saved && result.Result is { } runningResult
            ? Results.Created($"/api/running/results/{runningResult.Id}", Map(runningResult))
            : ResultStatus(result.Status, result.Result);
    }

    private static async Task<IResult> GetResultAsync(
        Guid resultId,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken) =>
        (await service.GetResultAsync(Owner(user), resultId, cancellationToken)) is { } result
            ? Results.Ok(Map(result))
            : Results.NotFound(MissingResult());

    private static async Task<IResult> UpdateResultAsync(
        Guid resultId,
        UpdateRunningResultRequest? request,
        ClaimsPrincipal user,
        IRunningService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("Resultat", "Angiv løbeturens oplysninger, og prøv igen.");
        }

        var result = await service.UpdateResultAsync(Owner(user), resultId, ToInput(request), cancellationToken);
        return result.Status is RunningStatus.Saved && result.Result is { } runningResult
            ? Results.Ok(Map(runningResult))
            : ResultStatus(result.Status, result.Result);
    }

    private static IResult PlanStatus(RunningStatus status) => status switch
    {
        RunningStatus.NotFound => Results.NotFound(MissingPlan()),
        RunningStatus.Conflict => Results.Conflict(new
        {
            title = "Løbeplanen er ændret i en anden fane. Dit udkast er bevaret; genindlæs før du prøver igen."
        }),
        RunningStatus.ReplacementRequired => Results.Conflict(new
        {
            title = "Du har allerede en aktiv løbeplan. Bekræft, at den skal erstattes, før du fortsætter."
        }),
        RunningStatus.Infeasible => Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Målet kan ikke nås realistisk med den valgte dato og ugentlige frekvens. Justér planens mål, dato eller træningsdage."),
        RunningStatus.Invalid => Validation("Plan", "Kontrollér niveau, distance på 30 minutter, måldistance, måldato, ugentlig frekvens og træningsdage."),
        _ => Unexpected()
    };

    private static IResult SessionStatus(RunningStatus status, RunningSessionData? currentSession = null) => status switch
    {
        RunningStatus.NotFound => Results.NotFound(MissingSession()),
        RunningStatus.Conflict => Results.Conflict(new
        {
            title = "Løbeturen er ændret i en anden fane. Genindlæs, og prøv igen.",
            sessionId = currentSession?.Id,
            resultId = currentSession?.Result?.Id
        }),
        RunningStatus.Invalid => Validation("Løbetur", "Løbeturen kan ikke ændres i dens nuværende tilstand."),
        _ => Unexpected()
    };

    private static IResult ResultStatus(RunningStatus status, RunningResultData? currentResult = null) => status switch
    {
        RunningStatus.NotFound => Results.NotFound(MissingResult()),
        RunningStatus.Conflict => Results.Conflict(new
        {
            title = "Resultatet er ændret i en anden fane. Dit udkast er bevaret; genindlæs før du prøver igen.",
            resultId = currentResult?.Id
        }),
        RunningStatus.Invalid => Validation("Resultat", "Kontrollér dato, distance, varighed, gennemsnitspuls og note."),
        _ => Unexpected()
    };

    private static IResult Validation(string field, string message) => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            [field] = [message]
        },
        title: "Kontrollér de indtastede oplysninger.");

    private static IResult Unexpected() => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: "Der opstod en uventet fejl.");

    private static object MissingPlan() => new { title = "Løbeplanen blev ikke fundet." };

    private static object MissingSession() => new { title = "Den planlagte løbetur blev ikke fundet." };

    private static object MissingResult() => new { title = "Det registrerede løberesultat blev ikke fundet." };

    private static bool TryReadDateRange(
        string? from,
        string? to,
        out DateOnly startDate,
        out DateOnly endDate,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        var hasStartDate = DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out startDate);
        var hasEndDate = DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out endDate);

        if (!hasStartDate)
        {
            errors["from"] = ["Angiv en gyldig startdato på formen ÅÅÅÅ-MM-DD."];
        }

        if (!hasEndDate)
        {
            errors["to"] = ["Angiv en gyldig slutdato på formen ÅÅÅÅ-MM-DD."];
        }

        if (!hasStartDate || !hasEndDate)
        {
            return false;
        }

        if (endDate < startDate)
        {
            errors["to"] = ["Slutdatoen skal ligge på eller efter startdatoen."];
        }
        else if (endDate.DayNumber - startDate.DayNumber + 1 > 93)
        {
            errors["to"] = ["Kalenderperioden må højst omfatte 93 dage."];
        }

        return errors.Count is 0;
    }

    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    private static RunningPlanInput ToInput(RunningPlanRequest request) => new(
        (FitnessApp.Domain.Running.RunningLevel)(int)request.Level,
        request.ThirtyMinuteDistanceKm,
        request.TargetDistanceKm,
        request.TargetDate,
        request.WeeklyFrequency,
        request.SelectedDays?.ToArray());

    private static ManualRunningResultInput ToInput(ManualRunningResultRequest request) => new(
        request.CompletionId,
        request.Date,
        request.DistanceKm,
        request.DurationSeconds,
        request.AverageHeartRate,
        request.Note);

    private static CompleteRunningSessionInput ToInput(CompleteRunningSessionRequest request) => new(
        request.CompletionId,
        request.Date,
        request.DistanceKm,
        request.DurationSeconds,
        request.AverageHeartRate,
        request.Note);

    private static UpdateRunningResultInput ToInput(UpdateRunningResultRequest request) => new(
        request.Version,
        request.Date,
        request.DistanceKm,
        request.DurationSeconds,
        request.AverageHeartRate,
        request.Note);

    private static RunningOverviewResponse Map(RunningOverviewData overview) => new(
        overview.ActivePlan is null ? null : Map(overview.ActivePlan),
        overview.UpcomingSessions.Select(Map).ToArray(),
        overview.Results.Select(Map).ToArray());

    private static RunningPlanResponse Map(RunningPlanData plan) => new(
        plan.Id,
        plan.Version,
        plan.IsActive,
        plan.CreatedAtUtc,
        plan.ReplacedAtUtc,
        (RunningLevel)(int)plan.Level,
        plan.ThirtyMinuteDistanceKm,
        plan.TargetDistanceKm,
        plan.TargetDate,
        plan.WeeklyFrequency,
        plan.SelectedDays.ToArray(),
        plan.Sessions.Select(Map).ToArray());

    private static RunningSessionResponse Map(RunningSessionData session) => new(
        session.Id,
        session.PlanId,
        session.Date,
        (RunningSessionKind)(int)session.Kind,
        session.PlannedDistanceKm,
        session.PaceMinSecondsPerKm,
        session.PaceMaxSecondsPerKm,
        session.Structure,
        session.StartedAtUtc,
        (FitnessApp.Contracts.Running.RunningSessionState)(int)session.State,
        session.Result is null ? null : Map(session.Result));

    private static RunningResultResponse Map(RunningResultData result) => new(
        result.Id,
        result.SessionId,
        result.Date,
        result.DistanceKm,
        result.DurationSeconds,
        result.AverageHeartRate,
        result.PaceSecondsPerKm,
        result.Note,
        result.Version,
        result.CreatedAtUtc,
        result.UpdatedAtUtc);
}
