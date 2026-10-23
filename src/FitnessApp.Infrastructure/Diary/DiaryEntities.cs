namespace FitnessApp.Infrastructure.Diary;

public sealed class DiaryEntry
{
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Ciphertext { get; set; } = "";
    public Guid Version { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
public sealed class DiaryProtection
{
    public string UserId { get; set; } = "";
    public string? CodeHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public Guid Revision { get; set; }
}
public sealed class DiaryGrant
{
    public string Hash { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public Guid Revision { get; set; }
    public DateTime LastActivityUtc { get; set; }
}
public sealed class DiaryMaintenance
{
    public int Id { get; set; }
    public DateOnly CompletedOnUtc { get; set; }
}
