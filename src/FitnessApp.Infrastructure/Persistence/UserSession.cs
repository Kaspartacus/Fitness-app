namespace FitnessApp.Infrastructure.Persistence;

public sealed class UserSession
{
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
}
