using FitnessApp.Infrastructure.Notifications;

namespace FitnessApp.Server.Notifications;

internal sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay startup so HTTP/migrations finish first; every tick creates an independent database scope.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Exceptions from providers may contain subscription endpoints/keys. Never log their contents.
                logger.LogWarning("Notification processing failed; persisted work will be retried within its delivery window.");
            }
        }
    }
}
