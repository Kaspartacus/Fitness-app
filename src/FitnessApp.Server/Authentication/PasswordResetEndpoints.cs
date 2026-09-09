using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using Microsoft.Extensions.Options;

namespace FitnessApp.Server.Authentication;

internal static class PasswordResetEndpoints
{
    internal const string NeutralMessage =
        "Hvis der findes en godkendt konto med denne e-mailadresse, modtager du en mail med et link til at nulstille din adgangskode.";
    private const string InvalidLinkMessage =
        "Linket er ugyldigt eller udløbet. Anmod om et nyt link og prøv igen.";
    private static readonly EventId RequestAccepted = new(1320, nameof(RequestAccepted));
    private static readonly EventId ResetRejected = new(1321, nameof(ResetRejected));

    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth/password-reset");
        group.MapPost("/request", RequestAsync)
            .AllowAnonymous()
            .RequireRateLimiting("password-reset");
        group.MapPost("/complete", ResetAsync)
            .AllowAnonymous()
            .RequireRateLimiting("password-reset");
        return endpoints;
    }

    private static async Task<IResult> RequestAsync(
        ForgotPasswordRequest request,
        HttpContext httpContext,
        IPasswordResetService passwordResetService,
        IOptions<PasswordResetOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var startedAt = timeProvider.GetTimestamp();
        await passwordResetService.RequestAsync(request.Email, cancellationToken);
        var elapsed = timeProvider.GetElapsedTime(startedAt);
        var remaining = TimeSpan.FromMilliseconds(options.Value.MinimumResponseMilliseconds) - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }

        loggerFactory.CreateLogger("PasswordReset").LogInformation(
            RequestAccepted,
            "Password reset request accepted without account disclosure. TraceId: {TraceId}",
            httpContext.TraceIdentifier);
        return Results.Accepted(value: new ForgotPasswordResponse(NeutralMessage));
    }

    private static async Task<IResult> ResetAsync(
        ResetPasswordRequest request,
        HttpContext httpContext,
        IPasswordResetService passwordResetService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var result = await passwordResetService.ResetAsync(
            request.Email,
            request.Token,
            request.NewPassword,
            cancellationToken);
        if (result.Status is PasswordResetStatus.Succeeded)
        {
            return Results.NoContent();
        }

        loggerFactory.CreateLogger("PasswordReset").LogWarning(
            ResetRejected,
            "Password reset was rejected. TraceId: {TraceId}",
            httpContext.TraceIdentifier);
        return result.Status is PasswordResetStatus.InvalidPassword
            ? Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.NewPassword)] = result.Errors.ToArray()
            })
            : Results.Json(
                new { title = InvalidLinkMessage },
                statusCode: StatusCodes.Status400BadRequest,
                contentType: "application/problem+json");
    }

    private static Dictionary<string, string[]> Validate(object request)
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
