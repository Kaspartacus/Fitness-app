using FitnessApp.Client.Settings;
using FitnessApp.Contracts.Notifications;

namespace FitnessApp.Client.Notifications;

public sealed class NotificationClient(SettingsClient api)
{
    public Task<SettingsClientResult<NotificationPageResponse>> InboxAsync(int page = 0) => api.SendAsync<NotificationPageResponse>(HttpMethod.Get, $"api/notifications?page={page}");
    public Task<SettingsClientResult<NotificationCountResponse>> UnreadAsync() => api.SendAsync<NotificationCountResponse>(HttpMethod.Get, "api/notifications/unread");
    public Task<SettingsClientResult<NotificationCountResponse>> ReadAsync(Guid[] ids) => api.SendAsync<NotificationCountResponse>(HttpMethod.Post, "api/notifications/read", new MarkNotificationsReadRequest(ids));
    public Task<SettingsClientResult<NotificationDestinationResponse>> DestinationAsync(Guid id) => api.SendAsync<NotificationDestinationResponse>(HttpMethod.Get, $"api/notifications/{id}/destination");
    public Task<SettingsClientResult<PushDeviceResponse>> DeviceAsync(string? binding) => api.SendAsync<PushDeviceResponse>(HttpMethod.Get, "api/notifications/device?binding=" + Uri.EscapeDataString(binding ?? ""));
    public Task<SettingsClientResult<NotificationActionResponse>> SubscribeAsync(SubscribePushRequest input) => api.SendAsync<NotificationActionResponse>(HttpMethod.Post, "api/notifications/subscribe", input);
    public Task<SettingsClientResult<NotificationActionResponse>> UnsubscribeAsync(string binding) => api.SendAsync<NotificationActionResponse>(HttpMethod.Post, "api/notifications/unsubscribe", new PushBindingRequest(binding));
    public Task<SettingsClientResult<NotificationActionResponse>> TestAsync(string binding) => api.SendAsync<NotificationActionResponse>(HttpMethod.Post, "api/notifications/test", new PushBindingRequest(binding));
}
