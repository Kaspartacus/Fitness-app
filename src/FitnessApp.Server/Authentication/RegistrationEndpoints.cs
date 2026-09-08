using System.ComponentModel.DataAnnotations;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.Server.Authentication;

internal static class RegistrationEndpoints
{
    private static readonly EventId RegistrationCreated = new(1100, nameof(RegistrationCreated));
    private static readonly EventId RegistrationAccepted = new(1101, nameof(RegistrationAccepted));

    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/registrations", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting("registration");

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        HttpContext httpContext,
        IRegistrationService registrationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var result = await registrationService.RegisterAsync(
            request.DisplayName,
            request.Email,
            request.Password,
            cancellationToken);

        if (result.Status is RegistrationStatus.Invalid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Password)] = result.Errors.ToArray()
            });
        }

        var logger = loggerFactory.CreateLogger("Registration");
        logger.LogInformation(
            result.Status is RegistrationStatus.Created ? RegistrationCreated : RegistrationAccepted,
            result.Status is RegistrationStatus.Created
                ? "Pending registration created. TraceId: {TraceId}"
                : "Registration request accepted without account disclosure. TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        return Results.Accepted();
    }

    private static Dictionary<string, string[]> Validate(RegistrationRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(memberName => new { memberName, result.ErrorMessage }))
            .GroupBy(error => error.memberName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage ?? "Feltet er ugyldigt.").ToArray());
    }
}
