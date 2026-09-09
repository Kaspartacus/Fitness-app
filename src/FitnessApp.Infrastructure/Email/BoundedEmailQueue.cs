using System.Threading.Channels;
using FitnessApp.Application.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitnessApp.Infrastructure.Email;

internal sealed class BoundedEmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> channel;

    public BoundedEmailQueue(IOptions<EmailDeliveryOptions> options)
    {
        channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryQueue(EmailMessage message) => channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class EmailDeliveryWorker(
    BoundedEmailQueue queue,
    IEmailSender sender,
    IOptions<EmailDeliveryOptions> options,
    ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    private static readonly EventId DeliverySucceeded = new(1310, nameof(DeliverySucceeded));
    private static readonly EventId DeliveryFailed = new(1311, nameof(DeliveryFailed));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= options.Value.MaxAttempts; attempt++)
            {
                try
                {
                    await sender.SendAsync(message, stoppingToken);
                    logger.LogInformation(
                        DeliverySucceeded,
                        "Email delivery succeeded. MessageId: {MessageId}; Attempt: {Attempt}",
                        message.MessageId,
                        attempt);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        DeliveryFailed,
                        "Email delivery failed. MessageId: {MessageId}; Attempt: {Attempt}; ErrorType: {ErrorType}",
                        message.MessageId,
                        attempt,
                        exception.GetType().Name);

                    if (attempt < options.Value.MaxAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(options.Value.RetryDelaySeconds),
                            stoppingToken);
                    }
                }
            }
        }
    }
}
