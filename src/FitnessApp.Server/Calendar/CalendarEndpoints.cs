using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Calendar;
using FitnessApp.Contracts.Calendar;
using Microsoft.AspNetCore.Mvc;
using CalendarContractActivityState = FitnessApp.Contracts.Calendar.CalendarActivityState;
using CalendarContractActivityType = FitnessApp.Contracts.Calendar.CalendarActivityType;

namespace FitnessApp.Server.Calendar;

internal static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar").RequireAuthorization();
        group.MapGet("", GetRangeAsync);
        group.MapPut("/running/{sessionId:guid}/move", MoveRunningOccurrenceAsync);
        group.MapPut("/strength/{programId:guid}/workouts/{workoutId:guid}/move", MoveStrengthOccurrenceAsync);
    }

    private static async Task<IResult> GetRangeAsync(
        string? from,
        string? to,
        ClaimsPrincipal user,
        ICalendarService service,
        CancellationToken cancellationToken)
    {
        if (!TryReadDateRange(from, to, out var startDate, out var endDate, out var errors))
        {
            return Results.ValidationProblem(errors, title: "Kontrollér datoperioden.");
        }

        var range = await service.GetRangeAsync(Owner(user), startDate, endDate, cancellationToken);
        return Results.Ok(new CalendarRangeResponse(range.Activities.Select(Map).ToArray()));
    }

    private static async Task<IResult> MoveRunningOccurrenceAsync(
        Guid sessionId,
        MoveCalendarOccurrenceRequest request,
        ClaimsPrincipal user,
        ICalendarService service,
        CancellationToken cancellationToken)
    {
        var result = await service.MoveRunningOccurrenceAsync(Owner(user), sessionId,
            new MoveCalendarOccurrenceInput(request.OriginalDate, request.TargetDate), cancellationToken);
        return MoveResult(result);
    }

    private static async Task<IResult> MoveStrengthOccurrenceAsync(
        Guid programId,
        Guid workoutId,
        MoveCalendarOccurrenceRequest request,
        ClaimsPrincipal user,
        ICalendarService service,
        CancellationToken cancellationToken)
    {
        var result = await service.MoveStrengthOccurrenceAsync(Owner(user), programId, workoutId,
            new MoveCalendarOccurrenceInput(request.OriginalDate, request.TargetDate), cancellationToken);
        return MoveResult(result);
    }

    private static CalendarActivityResponse Map(CalendarActivityData activity) => new(
        activity.Id,
        (CalendarContractActivityType)(int)activity.Type,
        (CalendarContractActivityState)(int)activity.State,
        activity.Date,
        activity.Title,
        activity.RunningKind is { } kind ? (FitnessApp.Contracts.Running.RunningSessionKind)(int)kind : null,
        activity.PlannedDistanceKm,
        activity.ActualDistanceKm,
        activity.ActualDurationSeconds,
        activity.PlannedDate,
        activity.RunningSessionId,
        activity.RunningResultId,
        activity.StrengthProgramId,
        activity.StrengthWorkoutId,
        activity.StrengthCompletionId);

    private static IResult MoveResult(CalendarMoveResult result) => result.Status switch
    {
        CalendarMoveStatus.Saved => Results.NoContent(),
        CalendarMoveStatus.NotFound => Results.NotFound(),
        CalendarMoveStatus.Invalid => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["targetDate"] = ["Vælg en anden dato i dag eller senere."]
            },
            title: "Træningen kan ikke flyttes."),
        CalendarMoveStatus.Conflict => Results.Conflict(),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

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

        return errors.Count == 0;
    }

    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
}
