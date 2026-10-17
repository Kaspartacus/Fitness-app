using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace FitnessApp.Server.Logging;

// One formatter for both console and the optional Alloy spool. Never serialize arbitrary
// state, scopes, exception messages or Exception.Data: these can contain private values.
internal sealed partial class StructuredConsoleFormatter(
    IHostEnvironment environment,
    LogSpool spool,
    IConfiguration configuration) : ConsoleFormatter("fitness-json")
{
    private readonly LogLevel dashboardConsoleMinimumLevel = configuration.GetValue(
        "Logging:Dashboard:ConsoleMinimumLevel", LogLevel.Warning);

    // EF and the request boundary may see the same object. Weak keys bound this to
    // the exception lifetime; later events retain only newly propagated stack frames.
    private readonly ConditionalWeakTable<Exception, ExceptionSnapshot> reportedExceptions = new();
    private readonly object exceptionLock = new();

    private static readonly HashSet<string> SafeFields =
        ["TraceId", "RequestId", "MigrationCount", "MessageId", "Attempt", "ErrorType", "StatusCode", "DurationMs", "Endpoint"];

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var fields = new Dictionary<string, object?>();
        scopes?.ForEachScope((scope, result) => CopySafeFields(scope, result), fields);
        CopySafeFields(entry.State, fields);
        var applicationEvent = entry.Category.StartsWith("FitnessApp.", StringComparison.Ordinal) ||
            entry.Category is "Authentication" or "PasswordReset" or "Registration" or "UserAdministration" or "UnhandledException";
        // Framework formatted messages can embed SQL, URLs, query strings and exception
        // text, even when EF sensitive-data logging is disabled. Keep their safe event name.
        var template = applicationEvent && entry.State is IEnumerable<KeyValuePair<string, object?>> state
            ? state.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string
            : null;
        var message = template is not null
            ? Placeholder().Replace(template, match => fields.TryGetValue(match.Groups[1].Value, out var value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "[redacted]")
            : entry.EventId.Name ?? "Framework event";
        object? exceptionDetails = null;
        var exceptionLoggedPreviously = false;
        object? additionalExceptionStack = null;
        if (entry.Exception is { } exception)
        {
            lock (exceptionLock)
            {
                exceptionLoggedPreviously = reportedExceptions.TryGetValue(exception, out _);
                if (!exceptionLoggedPreviously) exceptionDetails = ExceptionDetails(exception);
                else additionalExceptionStack = ExceptionDetails(exception);
            }
        }
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            severity = entry.LogLevel.ToString(),
            application = "FitnessApp",
            environment = environment.EnvironmentName,
            eventId = entry.EventId.Id,
            eventName = entry.EventId.Name,
            category = entry.Category,
            traceId = fields.GetValueOrDefault("TraceId"),
            requestId = fields.GetValueOrDefault("RequestId"),
            message,
            fields,
            exception = exceptionDetails,
            exceptionLoggedPreviously,
            additionalExceptionStack
        });
        var showConsole = entry.LogLevel >= dashboardConsoleMinimumLevel;
        var forwarded = spool.TryWrite(line, consoleFallback: !showConsole);
        // The provider must still receive Information events for the dashboard.
        // Filter only the console destination; fall back if spooling is unavailable.
        if (!forwarded || showConsole)
            writer.WriteLine(line);
    }

    private static void CopySafeFields(object? state, Dictionary<string, object?> fields)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs) return;
        foreach (var (key, value) in pairs)
        {
            if (!SafeFields.Contains(key)) continue;
            if (value is string text) fields[key] = text.Length <= 256 ? text : "[oversized]";
            else if (value is int or long or double or Guid) fields[key] = value;
        }
    }

    private object? ExceptionDetails(Exception? exception, int depth = 0)
    {
        if (exception is null) return null;
        if (reportedExceptions.TryGetValue(exception, out var previous))
        {
            var stack = exception.StackTrace ?? "";
            var oldFrames = previous.Stack.Split('\n').ToHashSet(StringComparer.Ordinal);
            var addedFrames = stack.Split('\n').Where(frame => !oldFrames.Contains(frame));
            previous.Stack = stack;
            return new { type = exception.GetType().FullName, previouslyLogged = true,
                additionalStackTrace = string.Join('\n', addedFrames) };
        }
        reportedExceptions.Add(exception, new ExceptionSnapshot { Stack = exception.StackTrace ?? "" });
        return new
        {
            type = exception.GetType().FullName,
            source = FindSource(exception),
            exception.HResult,
            // Stack frames and inner types are retained; free-form messages/data are not safe.
            stackTrace = exception.StackTrace,
            inner = depth < 8 ? ExceptionDetails(exception.InnerException, depth + 1) : null,
            aggregated = exception is AggregateException aggregate && depth < 8
                ? aggregate.InnerExceptions.Take(8).Select(inner => ExceptionDetails(inner, depth + 1)).ToArray() : null
        };
    }

    private static object? FindSource(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            var path = frame.GetFileName()?.Replace('\\', '/');
            var root = path?.LastIndexOf("/src/FitnessApp.", StringComparison.Ordinal) ?? -1;
            // Never publish an absolute build-machine path as a source field.
            var file = root >= 0 ? path![(root + 1)..]
                : path?.StartsWith("src/FitnessApp.", StringComparison.Ordinal) == true ? path : null;
            if (file is not null && (!file.EndsWith(".cs", StringComparison.Ordinal) ||
                file.Split('/').Any(part => part is "." or ".."))) file = null;
            var applicationFrame = type?.Assembly == typeof(StructuredConsoleFormatter).Assembly ||
                type?.Namespace?.StartsWith("FitnessApp.", StringComparison.Ordinal) == true;
            if (file is null && !applicationFrame) continue;
            var name = $"{type?.FullName}.{method?.Name}";
            // Async frames name the generated MoveNext method; show its source method.
            if (method?.Name == "MoveNext" && type?.DeclaringType is not null &&
                type.Name.StartsWith('<') && type.Name.IndexOf('>') is var end && end > 1)
                name = $"{type.DeclaringType.FullName}.{type.Name[1..end]}";
            int? line = file is not null && frame.GetFileLineNumber() > 0 ? frame.GetFileLineNumber() : null;
            return new { file, line, method = name,
                location = file is null ? name : line is null ? file : $"{file}:{line}" };
        }
        return null;
    }

    private sealed class ExceptionSnapshot
    {
        public required string Stack { get; set; }
    }

    [GeneratedRegex(@"\{([A-Za-z0-9]+)(?:[^}]*)\}")]
    private static partial Regex Placeholder();
}
