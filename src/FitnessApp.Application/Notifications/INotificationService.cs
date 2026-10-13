using FitnessApp.Application.Settings;

namespace FitnessApp.Application.Notifications;

public sealed record NotificationPageData(IReadOnlyList<InAppNotificationData> Items, bool HasMore, int UnreadCount);
public sealed record SubscribePushInput(string Endpoint, string P256dh, string Auth, string Binding, string InstallationId);
public sealed record PushDeviceData(bool Configured, string? PublicKey, bool Subscribed);
public enum PushSaveStatus { Saved, Invalid, Conflict, Limited, Unavailable, NotFound }
public sealed record NotificationDestinationData(string TargetPath, bool Unavailable);

public interface INotificationService
{
    Task<NotificationPageData> GetInboxAsync(string owner, int page, CancellationToken ct);
    Task<int> UnreadAsync(string owner, CancellationToken ct);
    Task MarkReadAsync(string owner, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<NotificationDestinationData> DestinationAsync(string owner, Guid id, CancellationToken ct);
    Task<PushDeviceData> DeviceAsync(string owner, string session, string? binding, CancellationToken ct);
    Task<PushSaveStatus> SubscribeAsync(string owner, string session, SubscribePushInput input, CancellationToken ct);
    Task UnsubscribeAsync(string owner, string binding, CancellationToken ct);
    Task<PushSaveStatus> TestAsync(string owner, string binding, CancellationToken ct);
}
