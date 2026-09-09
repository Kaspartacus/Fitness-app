using System.Collections.Concurrent;
using FitnessApp.Application.Email;
using Microsoft.Extensions.Logging;

namespace FitnessApp.IntegrationTests;

internal sealed class TestAppStorage : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        $"fitnessapp-tests-{Guid.NewGuid():N}");

    public TestAppStorage()
    {
        Directory.CreateDirectory(rootPath);
    }

    public string DatabasePath => Path.Combine(rootPath, "fitnessapp.db");

    public string KeyRingPath => Path.Combine(rootPath, "keys");

    public string PickupDirectory => Path.Combine(rootPath, "pickup");

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}

internal sealed class RecordingEmailSender(
    int failuresBeforeSuccess = 0,
    bool blockDeliveries = false) : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> messages = new();
    private readonly SemaphoreSlim delivered = new(0);
    private int remainingFailures = failuresBeforeSuccess;
    private int attempts;
    private readonly TaskCompletionSource deliveryRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyCollection<EmailMessage> Messages => messages.ToArray();

    public int Attempts => Volatile.Read(ref attempts);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attempts);
        if (blockDeliveries)
        {
            await deliveryRelease.Task.WaitAsync(cancellationToken);
        }

        if (Interlocked.Decrement(ref remainingFailures) >= 0)
        {
            throw new InvalidOperationException("Synthetic email transport failure.");
        }

        messages.Enqueue(message);
        delivered.Release();
    }

    public void ReleaseDeliveries() => deliveryRelease.TrySetResult();

    public async Task<EmailMessage> WaitForMessageAsync(CancellationToken cancellationToken = default)
    {
        if (messages.TryPeek(out var existing))
        {
            return existing;
        }

        var received = await delivered.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.True(received, "Timed out waiting for a captured email.");
        return messages.Last();
    }

    public async Task WaitForAttemptsAsync(int expected, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (Attempts < expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(Attempts >= expected, $"Expected {expected} delivery attempts, observed {Attempts}.");
    }
}

internal sealed class TestLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyCollection<string> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(entries, categoryName);

    public void Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<string> entries, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue($"{categoryName}|{eventId.Id}|{formatter(state, exception)}");
    }
}
