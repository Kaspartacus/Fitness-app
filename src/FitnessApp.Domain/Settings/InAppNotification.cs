namespace FitnessApp.Domain.Settings;

public enum InAppNotificationKind
{
    TrainingReminder,
    AdminRegistrationRequest, // Historical only; never generated automatically.
    MealReminder,
    NutritionProgress,
    PushTest
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
    public DateOnly? LocalDate { get; set; }
    public int? ScheduleSlot { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
    public long FoodSequence { get; set; }
    public long PreviousFoodSequence { get; set; }
}
