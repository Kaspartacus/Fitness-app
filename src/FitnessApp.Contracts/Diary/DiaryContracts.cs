namespace FitnessApp.Contracts.Diary;

public sealed record DiaryStatusResponse(bool Initialized, bool Protected, bool Unlocked, DateOnly Today, string TimeZoneId);
public sealed record DiaryDateResponse(DateOnly Date, Guid Version);
public sealed record DiaryEntryResponse(DateOnly Date, string Text, Guid Version);
public sealed record SaveDiaryRequest(string? Text, Guid? Version);
public sealed record DiaryCodeRequest(string? Code);
public sealed record ConfigureDiaryRequest(string? CurrentCode, string? NewCode, string? Confirmation);
public sealed record RecoverDiaryRequest(string? AccountPassword);
public sealed record DiaryUnlockResponse(string? Grant);
public sealed record DiaryErrorResponse(string Title);
