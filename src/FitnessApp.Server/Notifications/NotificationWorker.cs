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
                await scope.ServiceProvider.GetRequiredService<FitnessApp.Infrastructure.Diary.DiaryCleanup>().RunDailyAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<NotificationScheduler>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // The shared formatter retains type/stack, never provider messages or data.
                logger.LogWarning(new EventId(5100, "NotificationProcessingFailed"), exception, "Notification processing failed; persisted work will be retried within its delivery window.");
            }
        }
    }
}
