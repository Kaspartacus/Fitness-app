namespace FitnessApp.Application.Diary;

public enum DiaryStatus { Success, Invalid, Locked, Limited, Conflict, NotFound, Unavailable }
public sealed record DiaryResult<T>(DiaryStatus Status, T? Value = default);
public sealed record DiaryState(bool Initialized, bool Protected, bool Unlocked, DateOnly Today, string TimeZoneId);
public sealed record DiaryDate(DateOnly Date, Guid Version);
public sealed record DiaryEntryData(DateOnly Date, string Text, Guid Version);
public interface IDiaryService
{
    Task<DiaryResult<DiaryState>> StateAsync(string owner, string session, string? grant, CancellationToken ct);
    Task<DiaryResult<string?>> ConfigureAsync(string owner, string session, string? currentCode, string? newCode, string? confirmation, CancellationToken ct);
    Task<DiaryResult<string?>> UnlockAsync(string owner, string session, string? code, CancellationToken ct);
    Task<DiaryResult<bool>> RecoverAsync(string owner, string? accountPassword, CancellationToken ct);
    Task<DiaryResult<bool>> ActivityAsync(string owner, string session, string? grant, CancellationToken ct);
    Task LockAsync(string owner, string session, string? grant, CancellationToken ct);
    Task<DiaryResult<IReadOnlyList<DiaryDate>>> DatesAsync(string owner, string session, string? grant, int year, int month, CancellationToken ct);
    Task<DiaryResult<DiaryEntryData>> ReadAsync(string owner, string session, string? grant, DateOnly date, CancellationToken ct);
    Task<DiaryResult<DiaryEntryData>> SaveAsync(string owner, string session, string? grant, DateOnly date, string? text, Guid? version, CancellationToken ct);
    Task<DiaryResult<bool>> DeleteAsync(string owner, string session, string? grant, DateOnly date, Guid version, CancellationToken ct);
}
