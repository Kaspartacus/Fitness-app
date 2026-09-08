using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using FitnessApp.Domain.Users;

namespace FitnessApp.Server.Authentication;

internal static class UserAdministrationEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 100;
    private static readonly EventId RegistrationApproved = new(1200, nameof(RegistrationApproved));
    private static readonly EventId RegistrationRejected = new(1201, nameof(RegistrationRejected));
    private static readonly EventId RegistrationAlreadyDecided = new(1202, nameof(RegistrationAlreadyDecided));

    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/registrations")
            .RequireAuthorization(AuthenticationConstants.AdminPolicy);

        group.MapGet("/pending", GetPendingAsync);
        group.MapPost("/{registrationId}/approve", ApproveAsync);
        group.MapPost("/{registrationId}/reject", RejectAsync);

        return endpoints;
    }

    private static async Task<IResult> GetPendingAsync(
        int? limit,
        IUserAdministrationService administrationService,
        CancellationToken cancellationToken)
    {
        var requestedLimit = limit ?? DefaultLimit;
        if (requestedLimit is < 1 or > MaximumLimit)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(limit)] = [$"Antallet skal være mellem 1 og {MaximumLimit}."]
            });
        }

        var registrations = await administrationService.GetPendingAsync(requestedLimit, cancellationToken);
        return Results.Ok(new PendingRegistrationListResponse(
            registrations.Select(registration => new PendingRegistrationResponse(
                registration.Id,
                registration.DisplayName,
                registration.Email,
                registration.RegisteredAt)).ToArray(),
            requestedLimit));
    }

    private static Task<IResult> ApproveAsync(
        string registrationId,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IUserAdministrationService administrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        DecideAsync(
            registrationId,
            AccountApprovalStatus.Approved,
            principal,
            httpContext,
            administrationService,
            loggerFactory,
            cancellationToken);

    private static Task<IResult> RejectAsync(
        string registrationId,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IUserAdministrationService administrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        DecideAsync(
            registrationId,
            AccountApprovalStatus.Rejected,
            principal,
            httpContext,
            administrationService,
            loggerFactory,
            cancellationToken);

    private static async Task<IResult> DecideAsync(
        string registrationId,
        AccountApprovalStatus decision,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IUserAdministrationService administrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var administratorId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
        var result = await administrationService.DecideAsync(
            registrationId,
            decision,
            administratorId,
            cancellationToken);
        var logger = loggerFactory.CreateLogger("UserAdministration");

        if (result.Status is RegistrationDecisionStatus.Completed)
        {
            logger.LogInformation(
                decision is AccountApprovalStatus.Approved
                    ? RegistrationApproved
                    : RegistrationRejected,
                "Registration decision completed. RegistrationId: {RegistrationId}; AdministratorId: {AdministratorId}; TraceId: {TraceId}",
                registrationId,
                administratorId,
                httpContext.TraceIdentifier);
            return Results.NoContent();
        }

        if (result.Status is RegistrationDecisionStatus.AlreadyProcessed)
        {
            logger.LogInformation(
                RegistrationAlreadyDecided,
                "Registration decision was already completed. RegistrationId: {RegistrationId}; AdministratorId: {AdministratorId}; TraceId: {TraceId}",
                registrationId,
                administratorId,
                httpContext.TraceIdentifier);
            return Results.Conflict(new
            {
                title = "Registreringen er allerede behandlet.",
                status = result.CurrentStatus?.ToString()
            });
        }

        return Results.NotFound(new { title = "Registreringen blev ikke fundet." });
    }
}
