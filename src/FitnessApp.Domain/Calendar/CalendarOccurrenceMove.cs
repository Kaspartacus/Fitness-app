namespace FitnessApp.Domain.Calendar;

public enum CalendarOccurrenceKind
{
    Running,
    Strength
}

/// <summary>
/// Moves one planned occurrence without altering its recurring running or strength schedule.
/// </summary>
public sealed class CalendarOccurrenceMove
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public CalendarOccurrenceKind Kind { get; set; }
    public Guid ScopeId { get; set; }
    public Guid SourceId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
