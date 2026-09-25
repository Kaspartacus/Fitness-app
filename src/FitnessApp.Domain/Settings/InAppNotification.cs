namespace FitnessApp.Domain.Settings;

public enum InAppNotificationKind
{
    TrainingReminder,
    AdminRegistrationRequest
}

public sealed class InAppNotification
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public InAppNotificationKind Kind { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
