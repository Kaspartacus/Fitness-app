using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using FitnessApp.Application.Authentication;
using FitnessApp.Application.Email;
using FitnessApp.Domain.Users;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitnessApp.Infrastructure.Authentication;

internal sealed class PasswordResetService(
    UserManager<ApplicationUser> userManager,
    FitnessDbContext dbContext,
    IEmailQueue emailQueue,
    IOptions<PasswordResetOptions> resetOptions,
    IOptions<PublicAppOptions> publicAppOptions,
    TimeProvider timeProvider,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private static readonly EventId EmailQueued = new(1300, nameof(EmailQueued));
    private static readonly EventId EmailQueueFull = new(1301, nameof(EmailQueueFull));
    private static readonly EventId PasswordResetCompleted = new(1302, nameof(PasswordResetCompleted));
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResetLocks = new();

    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || user.ApprovalStatus is not AccountApprovalStatus.Approved)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var cooldownCutoff = now.AddSeconds(-resetOptions.Value.CooldownSeconds).UtcDateTime;
        var reserved = await dbContext.Users
            .Where(candidate =>
                candidate.Id == user.Id &&
                candidate.ApprovalStatus == AccountApprovalStatus.Approved &&
                (candidate.LastPasswordResetEmailQueuedAt == null ||
                 candidate.LastPasswordResetEmailQueuedAt <= cooldownCutoff))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.LastPasswordResetEmailQueuedAt,
                    now.UtcDateTime),
                cancellationToken);

        if (reserved is 0)
        {
            return;
        }

        dbContext.ChangeTracker.Clear();
        user = await userManager.FindByIdAsync(user.Id);
        if (user is null || user.ApprovalStatus is not AccountApprovalStatus.Approved)
        {
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var resetUrl = QueryHelpers.AddQueryString(
            $"{publicAppOptions.Value.BaseUrl.TrimEnd('/')}/nulstil-adgangskode",
            new Dictionary<string, string?>
            {
                ["email"] = user.Email,
                ["token"] = encodedToken
            });
        var lifetimeMinutes = resetOptions.Value.TokenLifetimeMinutes;
        var lifetimeText = lifetimeMinutes == 60
            ? "1 time"
            : $"{lifetimeMinutes} minutter";
        var safeUrl = HtmlEncoder.Default.Encode(resetUrl);
        var message = new EmailMessage(
            Guid.NewGuid().ToString("N"),
            user.Email!,
            "Nulstil din adgangskode til FitnessApp",
            $"Nulstil din adgangskode til FitnessApp:\n\n{resetUrl}\n\nLinket udløber om {lifetimeText}. Hvis du ikke bad om dette, kan du ignorere mailen.",
            $"<p>Du har bedt om at nulstille din adgangskode til FitnessApp.</p><p><a href=\"{safeUrl}\">Nulstil adgangskode</a></p><p>Linket udløber om {lifetimeText}. Hvis du ikke bad om dette, kan du ignorere mailen.</p>");

        if (emailQueue.TryQueue(message))
        {
            logger.LogInformation(
                EmailQueued,
                "Password reset email queued. MessageId: {MessageId}",
                message.MessageId);
            return;
        }

        logger.LogWarning(
            EmailQueueFull,
            "Password reset email queue was full. MessageId: {MessageId}",
            message.MessageId);
        await dbContext.Users
            .Where(candidate =>
                candidate.Id == user.Id &&
                candidate.LastPasswordResetEmailQueuedAt == now.UtcDateTime)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.LastPasswordResetEmailQueuedAt,
                    (DateTime?)null),
                cancellationToken);
    }

    public async Task<PasswordResetResult> ResetAsync(
        string email,
        string encodedToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return PasswordResetResult.InvalidOrExpired();
        }

        var normalizedEmail = email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || user.ApprovalStatus is not AccountApprovalStatus.Approved)
        {
            return PasswordResetResult.InvalidOrExpired();
        }

        var resetLock = ResetLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await resetLock.WaitAsync(cancellationToken);
        try
        {
            dbContext.ChangeTracker.Clear();
            user = await userManager.FindByIdAsync(user.Id);
            if (user is null || user.ApprovalStatus is not AccountApprovalStatus.Approved)
            {
                return PasswordResetResult.InvalidOrExpired();
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var reset = await userManager.ResetPasswordAsync(user, token, newPassword);
            if (!reset.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                if (reset.Errors.Any(error => error.Code is "InvalidToken" or "ConcurrencyFailure"))
                {
                    return PasswordResetResult.InvalidOrExpired();
                }

                return PasswordResetResult.InvalidPassword(
                    reset.Errors.Select(ToDanishPasswordMessage).Distinct().ToArray());
            }

            var resetAt = timeProvider.GetUtcNow();
            await dbContext.UserSessions
                .Where(session => session.UserId == user.Id && session.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(session => session.RevokedAt, resetAt),
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                PasswordResetCompleted,
                "Password reset completed and all sessions revoked. UserId: {UserId}",
                user.Id);
            return PasswordResetResult.Succeeded();
        }
        catch (DbUpdateConcurrencyException)
        {
            return PasswordResetResult.InvalidOrExpired();
        }
        finally
        {
            resetLock.Release();
        }
    }

    private static string ToDanishPasswordMessage(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Adgangskoden er for kort.",
        "PasswordRequiresDigit" => "Adgangskoden skal indeholde et tal.",
        "PasswordRequiresLower" => "Adgangskoden skal indeholde et lille bogstav.",
        "PasswordRequiresUpper" => "Adgangskoden skal indeholde et stort bogstav.",
        "PasswordRequiresNonAlphanumeric" => "Adgangskoden skal indeholde et specialtegn.",
        _ => "Adgangskoden opfylder ikke kravene."
    };
}
