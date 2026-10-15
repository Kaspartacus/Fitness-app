namespace FitnessApp.Infrastructure.Persistence;

public sealed class UserSession
{
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    public string? RefreshTokenHash { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? SecurityStamp { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

// Consumed hashes remain until the device session is deleted, so old-token replay can revoke it.
public sealed class ConsumedRefreshToken
{
    public string Hash { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset ConsumedAt { get; set; }
}
