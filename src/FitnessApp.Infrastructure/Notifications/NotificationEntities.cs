namespace FitnessApp.Infrastructure.Notifications;

public sealed class NotificationOccurrence
{
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Slot { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
}

// An append-only journal. A database trigger records insertions in the same transaction as food saves.
// No foreign key to entries: deleting or editing food must never erase or increment this sequence.
public sealed class NutritionLogAddition
{
    public long Sequence { get; set; }
    public Guid EntryId { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
}

public sealed class WebPushSubscription
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string Binding { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class NotificationDelivery
{
    public Guid NotificationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public bool Finished { get; set; }
}
