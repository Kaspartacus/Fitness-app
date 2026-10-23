using System.Security.Cryptography;
using System.Text;
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
    IRefreshCredentialDeriver refreshCredentialDeriver,
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
        var credential = CreateCredential();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            LastActivityAt = now,
            RefreshTokenHash = Hash(credential),
            SecurityStamp = user.SecurityStamp
        };

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(LoginStatus.Succeeded, accessToken, authenticatedUser, new RefreshCredential(credential, session.ExpiresAt), session.Id);
    }

    private static string CreateCredential() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string credential) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    public async Task<RefreshResult> RefreshAsync(string credential, bool userActivity, string? expectedSessionId, CancellationToken cancellationToken)
    {
        if (credential.Length != 64) return new(RefreshStatus.Rejected);
        var hash = Hash(credential);
        var now = timeProvider.GetUtcNow();
        // A database transaction, not a process-local lock, serializes rotations with revocation
        // and password reset. Retries may only reproduce the exact unacknowledged successor.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var session = await dbContext.UserSessions.Include(s => s.User)
            .SingleOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);
        if (session is null)
        {
            var consumed = await dbContext.ConsumedRefreshTokens.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Hash == hash, cancellationToken);
            if (consumed is null) return new(RefreshStatus.Rejected);
            var consumedSession = await dbContext.UserSessions.AsNoTracking().Include(s => s.User)
                .SingleAsync(s => s.Id == consumed.SessionId, cancellationToken);
            if (expectedSessionId is not null && expectedSessionId != consumedSession.Id) return new(RefreshStatus.Conflict);
            if (consumedSession.RevokedAt is not null || consumedSession.ExpiresAt <= now ||
                consumedSession.SecurityStamp != consumedSession.User.SecurityStamp ||
                consumedSession.User.ApprovalStatus != AccountApprovalStatus.Approved)
                return new(RefreshStatus.Rejected);
            var successor = refreshCredentialDeriver.DeriveSuccessor(credential);
            if (Hash(successor) == consumedSession.RefreshTokenHash)
            {
                // The successor has never been presented: the prior response/cookie may have
                // been lost. Recover that exact child without rotating or crediting activity.
                // This remains possible through a temporary outage, but never beyond inactivity.
                var recoveredUser = await ValidateSessionAsync(consumedSession.UserId, consumedSession.Id, cancellationToken);
                if (recoveredUser is null) return new(RefreshStatus.Rejected);
                var recoveredAccessToken = tokenIssuer.Issue(recoveredUser, consumedSession.Id);
                await transaction.CommitAsync(cancellationToken);
                return new(RefreshStatus.Succeeded, recoveredAccessToken, recoveredUser,
                    new RefreshCredential(successor, consumedSession.ExpiresAt), consumedSession.Id);
            }
            // A later credential was presented, acknowledging the earlier cookie. Older
            // requests may briefly be concurrent; they get no cookie/JWT, then replay revokes.
            if (now - consumed.ConsumedAt <= TimeSpan.FromSeconds(30)) return new(RefreshStatus.Conflict);
            await dbContext.UserSessions.Where(s => s.Id == consumed.SessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);
            await dbContext.WebPushSubscriptions.Where(s => s.SessionId == consumed.SessionId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RefreshStatus.Rejected);
        }
        if (expectedSessionId is not null && expectedSessionId != session.Id) return new(RefreshStatus.Conflict);
        if (session.RevokedAt is not null || session.ExpiresAt <= now ||
            session.SecurityStamp != session.User.SecurityStamp ||
            session.User.ApprovalStatus != AccountApprovalStatus.Approved)
            return new(RefreshStatus.Rejected);

        var next = refreshCredentialDeriver.DeriveSuccessor(credential);
        var expiresAt = session.ExpiresAt;
        // At most one inactivity write every minute; the deadline is always based on real activity.
        if (userActivity && now - session.LastActivityAt >= TimeSpan.FromMinutes(1))
        {
            session.LastActivityAt = now;
            expiresAt = now.AddDays(30);
            session.ExpiresAt = expiresAt;
        }
        session.RefreshTokenHash = Hash(next);
        dbContext.ConsumedRefreshTokens.Add(new ConsumedRefreshToken { Hash = hash, SessionId = session.Id, ConsumedAt = now });
        await dbContext.SaveChangesAsync(cancellationToken);
        var user = await ValidateSessionAsync(session.UserId, session.Id, cancellationToken);
        if (user is null) return new(RefreshStatus.Rejected);
        var accessToken = tokenIssuer.Issue(user, session.Id);
        await transaction.CommitAsync(cancellationToken);
        return new(RefreshStatus.Succeeded, accessToken, user, new RefreshCredential(next, expiresAt), session.Id);
    }

    public async Task LogoutCredentialAsync(string credential, CancellationToken cancellationToken)
    {
        if (credential.Length != 64) return;
        var hash = Hash(credential);
        var session = await dbContext.UserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);
        var sessionId = session?.Id ?? await dbContext.ConsumedRefreshTokens.Where(t => t.Hash == hash).Select(t => t.SessionId).SingleOrDefaultAsync(cancellationToken);
        if (sessionId is null) return;
        var now = timeProvider.GetUtcNow();
        await dbContext.UserSessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);
        await dbContext.WebPushSubscriptions.Where(s => s.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.DiaryGrants.Where(g => g.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);
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

        await dbContext.WebPushSubscriptions.Where(x => x.UserId == userId && x.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);
        session.RevokedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
