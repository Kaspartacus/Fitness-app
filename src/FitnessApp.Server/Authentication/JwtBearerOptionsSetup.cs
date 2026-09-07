using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FitnessApp.Server.Authentication;

internal sealed class JwtBearerOptionsSetup(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is not JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var settings = jwtOptions.Value;
        var signingKey = new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey));

        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidateSessionAsync,
            OnChallenge = context =>
            {
                AuthenticationHttpResponses.SetNoStore(context.Response);
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                AuthenticationHttpResponses.SetNoStore(context.Response);
                return Task.CompletedTask;
            }
        };
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var sessionId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sid);
        if (userId is null || sessionId is null)
        {
            context.Fail("Required token claims are missing.");
            return;
        }

        var authenticationService = context.HttpContext.RequestServices
            .GetRequiredService<IAuthenticationService>();
        var currentUser = await authenticationService.ValidateSessionAsync(
            userId,
            sessionId,
            context.HttpContext.RequestAborted);

        if (currentUser is null)
        {
            context.Fail("The session is no longer valid.");
            return;
        }

        var retainedClaims = context.Principal!.Claims
            .Where(claim => claim.Type is not ClaimTypes.Role and not ClaimTypes.Email and not ClaimTypes.Name)
            .ToList();
        retainedClaims.Add(new Claim(ClaimTypes.Email, currentUser.Email));
        retainedClaims.Add(new Claim(ClaimTypes.Name, currentUser.DisplayName));
        retainedClaims.AddRange(currentUser.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            retainedClaims,
            JwtBearerDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }
}
