using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Notifications;
using FitnessApp.Contracts.Notifications;
using FitnessApp.Contracts.Settings;

namespace FitnessApp.Server.Notifications;

internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/notifications").RequireAuthorization().RequireRateLimiting("notifications");
        group.MapGet("", async (int? page, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
        {
            if (page is < 0 or > 100000) return Results.BadRequest();
            var data = await service.GetInboxAsync(Owner(user), page ?? 0, ct);
            return Results.Ok(new NotificationPageResponse(data.Items.Select(n => new InAppNotificationResponse(n.Id, n.Kind, n.Title, n.Message, n.TargetPath, n.CreatedAt, n.ReadAt)).ToArray(), data.HasMore, data.UnreadCount));
        });
        group.MapGet("/unread", async (ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
            Results.Ok(new NotificationCountResponse(await service.UnreadAsync(Owner(user), ct))));
        group.MapPost("/read", async (MarkNotificationsReadRequest request, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
        {
            if (request.Ids is null || request.Ids.Length > 25) return Results.BadRequest();
            await service.MarkReadAsync(Owner(user), request.Ids, ct);
            return Results.Ok(new NotificationCountResponse(await service.UnreadAsync(Owner(user), ct)));
        });
        group.MapGet("/{id:guid}/destination", async (Guid id, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
        {
            var result = await service.DestinationAsync(Owner(user), id, ct);
            return Results.Ok(new NotificationDestinationResponse(result.TargetPath, result.Unavailable));
        });
        group.MapGet("/device", async (string? binding, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
        {
            var result = await service.DeviceAsync(Owner(user), Session(user), binding, ct);
            return Results.Ok(new PushDeviceResponse(result.Configured, result.PublicKey, result.Subscribed));
        });
        group.MapPost("/subscribe", async (SubscribePushRequest request, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
            Map(await service.SubscribeAsync(Owner(user), Session(user), new(request.Endpoint, request.P256dh, request.Auth, request.Binding, request.InstallationId), ct)));
        group.MapPost("/unsubscribe", async (PushBindingRequest request, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
        {
            await service.UnsubscribeAsync(Owner(user), request.Binding, ct);
            return Results.Ok(new NotificationActionResponse(true));
        });
        group.MapPost("/test", async (PushBindingRequest request, ClaimsPrincipal user, INotificationService service, CancellationToken ct) =>
            Map(await service.TestAsync(Owner(user), request.Binding, ct))).RequireRateLimiting("push-test");
        return routes;
    }
    private static IResult Map(PushSaveStatus status) => status switch
    {
        PushSaveStatus.Saved => Results.Ok(new NotificationActionResponse(true)),
        PushSaveStatus.Invalid => Results.BadRequest(new { title = "Enheden kunne ikke tilmeldes. Kontrollér browserens understøttelse." }),
        PushSaveStatus.Conflict => Results.Conflict(new { title = "Slå telefonnotifikationer fra og til igen på denne enhed." }),
        PushSaveStatus.Limited => Results.Json(new { title = "Vent fem minutter før næste test, eller fjern en gammel enhed (højst 10)." }, statusCode: 429),
        PushSaveStatus.Unavailable => Results.Json(new { title = "Telefonnotifikationer er ikke konfigureret på serveren endnu." }, statusCode: 503),
        _ => Results.NotFound(new { title = "Enheden er ikke tilmeldt." })
    };
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
    private static string Session(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sid)!;
}
