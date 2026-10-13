using FitnessApp.Contracts.Settings;

namespace FitnessApp.Contracts.Notifications;

public sealed record NotificationPageResponse(IReadOnlyList<InAppNotificationResponse> Items, bool HasMore, int UnreadCount);
public sealed record NotificationCountResponse(int UnreadCount);
public sealed record MarkNotificationsReadRequest(Guid[] Ids);
public sealed record SubscribePushRequest(string Endpoint, string P256dh, string Auth, string Binding, string InstallationId);
public sealed record PushDeviceResponse(bool Configured, string? PublicKey, bool Subscribed);
public sealed record PushBindingRequest(string Binding);
public sealed record NotificationDestinationResponse(string TargetPath, bool Unavailable);
public sealed record NotificationActionResponse(bool Succeeded);
