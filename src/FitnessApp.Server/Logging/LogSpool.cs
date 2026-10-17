using System.Text;
using System.Threading.Channels;
using FitnessApp.Server.Configuration;

namespace FitnessApp.Server.Logging;

// Optional local transport buffer, not another logging framework. Network access and
// retries belong exclusively to Alloy. TryWrite never waits for disk or a collector.
internal sealed class LogSpool(IConfiguration configuration, IWebHostEnvironment environment, IServiceProvider services) : BackgroundService
{
    private const int FileCount = 4;
    private const int FileBytes = 5 * 1024 * 1024;
    private const int EntryBytes = 64 * 1024;
    private readonly Channel<PendingLog> queue = Channel.CreateBounded<PendingLog>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly bool enabled = configuration.GetValue<bool>("Logging:Dashboard:Enabled");
    private volatile bool ready;

    public bool TryWrite(string line, bool consoleFallback)
    {
        if (!ready) return false;
        // Bound queued memory as well as file sizes. Oversized events remain on console.
        return Encoding.UTF8.GetByteCount(line) <= EntryBytes && queue.Writer.TryWrite(new PendingLog(line, consoleFallback));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
    {
        if (!enabled) return;
        PendingLog? pending = null;
        try
        {
            var directory = PrivateDirectory.ResolveAndCreate("App_Data/logs", environment.ContentRootPath,
                environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "Logging spool");
            // Reuse a fixed set of names across restarts; do not accumulate date-stamped files.
            var index = SelectFile(directory, newest: true);
            await using var initial = Open(directory, index, append: true);
            FileStream current = initial;
            // Until the directory/file is open, startup events stay on the console.
            ready = true;
            try
            {
                await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken))
                {
                    pending = entry;
                    var bytes = Encoding.UTF8.GetBytes(entry.Line + "\n");
                    if (current.Length + bytes.Length > FileBytes)
                    {
                        await current.DisposeAsync();
                        index = SelectFile(directory, newest: false, excludedIndex: index);
                        current = Open(directory, index, append: false);
                    }
                    await current.WriteAsync(bytes, stoppingToken);
                    await current.FlushAsync(stoppingToken);
                    pending = null;
                }
            }
            finally { await current.DisposeAsync(); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Disable until restart, with one safe diagnostic. Never retry or report file paths.
            ready = false;
            queue.Writer.TryComplete();
            services.GetRequiredService<ILogger<LogSpool>>().LogWarning(
                new EventId(5200, "DashboardSpoolUnavailable"), exception,
                "Dashboard log spool unavailable; console logging continues. Restart to retry.");
        }
        finally
        {
            ready = false;
            queue.Writer.TryComplete();
            // Admission is not delivery: replay only entries whose console copy was
            // suppressed. Completion prevents concurrent writers adding after this drain.
            WriteFallback(pending);
            while (queue.Reader.TryRead(out var entry)) WriteFallback(entry);
        }
    }, CancellationToken.None);

    private readonly record struct PendingLog(string Line, bool ConsoleFallback);

    private static void WriteFallback(PendingLog? entry)
    {
        if (entry is not { ConsoleFallback: true } value) return;
        // Already privacy-filtered JSON. Bypass ILogger to avoid re-enqueueing the
        // abandoned event or repeating exception serialization/deduplication.
        try { Console.Error.WriteLine(value.Line); }
        catch (IOException) { /* The console is unavailable too; never retry. */ }
        catch (ObjectDisposedException) { /* The host may be shutting down. */ }
    }

    private static int SelectFile(string directory, bool newest, int? excludedIndex = null)
    {
        // Resume the last active file after restart. On rotation, reuse a missing
        // slot first, then the oldest retained file; never truncate the active one.
        var files = Enumerable.Range(0, FileCount)
            .Where(index => index != excludedIndex)
            .Select(index => new
            {
                Index = index,
                Modified = File.Exists(Path.Combine(directory, $"fitness-{index}.jsonl"))
                    ? File.GetLastWriteTimeUtc(Path.Combine(directory, $"fitness-{index}.jsonl"))
                    : DateTime.MinValue
            });
        var ordered = newest
            ? files.OrderByDescending(file => file.Modified)
            : files.OrderBy(file => file.Modified);
        return ordered.ThenBy(file => file.Index).First().Index;
    }

    private static FileStream Open(string directory, int index, bool append)
    {
        var options = new FileStreamOptions
        {
            Mode = append ? FileMode.OpenOrCreate : FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.Read | FileShare.Delete,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(Path.Combine(directory, $"fitness-{index}.jsonl"), options);
        if (append) stream.Seek(0, SeekOrigin.End);
        return stream;
    }
}
