using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FitnessApp.Server.Authentication;

internal sealed class JwtAccessTokenIssuer(
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AuthenticatedUser user, string sessionId)
    {
        var settings = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Sid, sessionId)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
