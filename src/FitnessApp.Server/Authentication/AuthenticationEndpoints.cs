using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.Server.Authentication;

internal static class AuthenticationEndpoints
{
    private static readonly EventId LoginSucceeded = new(1000, nameof(LoginSucceeded));
    private static readonly EventId LoginFailed = new(1001, nameof(LoginFailed));
    private static readonly EventId LoginLockedOut = new(1002, nameof(LoginLockedOut));
    private static readonly EventId LogoutSucceeded = new(1003, nameof(LogoutSucceeded));

    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");
        group.MapGet("/me", CurrentUser)
            .RequireAuthorization();
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IAuthenticationService authenticationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
        {
            var errors = validationResults
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                    .Select(memberName => new { memberName, result.ErrorMessage }))
                .GroupBy(error => error.memberName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage ?? "Feltet er ugyldigt.").ToArray());
            return Results.ValidationProblem(errors);
        }

        var logger = loggerFactory.CreateLogger("Authentication");
        var result = await authenticationService.LoginAsync(
            request.Email.Trim(),
            request.Password,
            cancellationToken);

        if (result.Status is LoginStatus.Succeeded)
        {
            logger.LogInformation(
                LoginSucceeded,
                "Login succeeded. TraceId: {TraceId}",
                httpContext.TraceIdentifier);

            var user = result.User!;
            return Results.Ok(new LoginResponse(
                result.AccessToken!.Value,
                result.AccessToken.ExpiresAt,
                new AuthenticatedUserResponse(user.Email, user.DisplayName, user.Roles)));
        }

        var eventId = result.Status is LoginStatus.LockedOut ? LoginLockedOut : LoginFailed;
        var message = result.Status is LoginStatus.LockedOut
            ? "Login was blocked by account lockout. TraceId: {TraceId}"
            : "Login failed. TraceId: {TraceId}";
        logger.LogWarning(eventId, message, httpContext.TraceIdentifier);

        return Results.Json(
            new { title = "E-mail eller adgangskode er forkert, eller kontoen har ikke adgang." },
            statusCode: StatusCodes.Status401Unauthorized,
            contentType: "application/problem+json");
    }

    private static IResult CurrentUser(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)!;
        var displayName = principal.FindFirstValue(ClaimTypes.Name)!;
        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return Results.Ok(new AuthenticatedUserResponse(email, displayName, roles));
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IAuthenticationService authenticationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
        var sessionId = principal.FindFirstValue(JwtRegisteredClaimNames.Sid)!;
        await authenticationService.LogoutAsync(userId, sessionId, cancellationToken);

        loggerFactory.CreateLogger("Authentication").LogInformation(
            LogoutSucceeded,
            "Logout completed. TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        return Results.NoContent();
    }
}
