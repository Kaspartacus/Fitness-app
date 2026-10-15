using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using FitnessApp.Application.Authentication;
using FitnessApp.Contracts.Authentication;
using Microsoft.Extensions.Options;

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
        group.AddEndpointFilter(async (context, next) =>
        {
            AuthenticationHttpResponses.SetNoStore(context.HttpContext.Response);
            if (HttpMethods.IsPost(context.HttpContext.Request.Method) &&
                !RefreshCookie.IsTrusted(context.HttpContext.Request,
                    context.HttpContext.RequestServices.GetRequiredService<IOptions<PublicAppOptions>>()))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return await next(context);
        });

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("session-refresh");
        group.MapGet("/me", CurrentUser)
            .RequireAuthorization();
        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous();

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

            // A successful account switch revokes the cookie's prior device session before replacement.
            if (httpContext.Request.Cookies[RefreshCookie.Name] is { } previous)
                await authenticationService.LogoutCredentialAsync(previous, cancellationToken);
            RefreshCookie.Write(httpContext.Response, result.Refresh!);
            var user = result.User!;
            return Results.Ok(new LoginResponse(
                result.AccessToken!.Value,
                result.AccessToken.ExpiresAt,
                new AuthenticatedUserResponse(user.Email, user.DisplayName, user.Roles), result.SessionId));
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

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request, HttpContext context, IAuthenticationService service, CancellationToken cancellationToken)
    {
        var credential = context.Request.Cookies[RefreshCookie.Name];
        if (credential is null) return Results.Unauthorized();
        var result = await service.RefreshAsync(credential, request.UserActivity, request.ExpectedSessionId, cancellationToken);
        if (result.Status == RefreshStatus.Conflict) return Results.Conflict();
        // Do not delete on rejection: a delayed response must never erase a newer tab's cookie.
        if (result.Status != RefreshStatus.Succeeded) return Results.Unauthorized();
        RefreshCookie.Write(context.Response, result.Refresh!);
        var user = result.User!;
        return Results.Ok(new LoginResponse(result.AccessToken!.Value, result.AccessToken.ExpiresAt,
            new AuthenticatedUserResponse(user.Email, user.DisplayName, user.Roles), result.SessionId));
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
        if (httpContext.Request.Cookies[RefreshCookie.Name] is { } credential)
            await authenticationService.LogoutCredentialAsync(credential, cancellationToken);
        if (principal.FindFirstValue(JwtRegisteredClaimNames.Sub) is { } userId &&
            principal.FindFirstValue(JwtRegisteredClaimNames.Sid) is { } sessionId)
            await authenticationService.LogoutAsync(userId, sessionId, cancellationToken);
        RefreshCookie.Delete(httpContext.Response);

        loggerFactory.CreateLogger("Authentication").LogInformation(
            LogoutSucceeded,
            "Logout completed. TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        return Results.NoContent();
    }
}
