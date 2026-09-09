using FitnessApp.Application.Authentication;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Authentication;

internal sealed class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    FitnessDbContext dbContext,
    IAccessTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : IAuthenticationService
{
    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);

        if (passwordResult.IsLockedOut)
        {
            return new LoginResult(LoginStatus.LockedOut);
        }

        if (!passwordResult.Succeeded)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (user.ApprovalStatus is not AccountApprovalStatus.Approved)
        {
            return new LoginResult(LoginStatus.NotApproved);
        }

        var roles = await userManager.GetRolesAsync(user);
        var authenticatedUser = new AuthenticatedUser(
            user.Id,
            user.Email!,
            user.DisplayName,
            roles.ToArray());

        var now = timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid().ToString("N");
        var accessToken = tokenIssuer.Issue(authenticatedUser, sessionId);
        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = accessToken.ExpiresAt,
            SecurityStamp = user.SecurityStamp
        };

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(LoginStatus.Succeeded, accessToken, authenticatedUser);
    }

    public async Task<AuthenticatedUser?> ValidateSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = await dbContext.UserSessions
            .AsNoTracking()
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == sessionId && candidate.UserId == userId,
                cancellationToken);

        if (session is null ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= now ||
            (session.SecurityStamp is not null &&
             session.SecurityStamp != session.User.SecurityStamp) ||
            session.User.ApprovalStatus is not AccountApprovalStatus.Approved)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(session.User);
        return new AuthenticatedUser(
            session.User.Id,
            session.User.Email!,
            session.User.DisplayName,
            roles.ToArray());
    }

    public async Task<bool> LogoutAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            candidate => candidate.Id == sessionId && candidate.UserId == userId,
            cancellationToken);

        if (session is null || session.RevokedAt is not null)
        {
            return false;
        }

        session.RevokedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
