namespace FitnessApp.Domain.Diary;

public static class DiaryRules
{
    public const int MaximumTextLength = 12_000;
    public const int MaximumCodeLength = 128;
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(15);
    public static bool ValidCode(string? code) => code is { Length: >= 1 and <= MaximumCodeLength };
    public static DateOnly ExpiryDate(DateOnly diaryDate) => diaryDate.AddYears(1);
    public static bool ShouldRemind(bool enabled, bool hasContent) => enabled && !hasContent;
}
