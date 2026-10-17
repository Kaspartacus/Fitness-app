# Local logs with Grafana, Loki and Alloy

This optional stack runs **only monitoring** in Docker. The existing .NET 10 single-server workflow, HTTPS certificate and User Secrets setup in the root [README](../README.md) still apply. No app container, browser telemetry, tracing backend or hosted service is added.

## Start and stop

Run from the repository root. Install Docker Engine/Desktop with Compose v2+ separately. Create a random Grafana bootstrap password without printing it:

```bash
umask 077
mkdir -p monitoring/.secrets src/FitnessApp.Server/App_Data/logs
chmod 700 monitoring/.secrets src/FitnessApp.Server/App_Data/logs
openssl rand -base64 36 > monitoring/.secrets/grafana-admin-password
chmod 444 monitoring/.secrets/grafana-admin-password
docker compose -f monitoring/compose.yaml up -d
Logging__Dashboard__Enabled=true dotnet run --project src/FitnessApp.Server
```

The password file is read-only so Grafana's container UID can read the Compose secret. Its parent directory is owner-only, and `.secrets/` is ignored. Use a local password manager to retain the generated value; do not paste credentials into shell arguments, tracked files, screenshots or issue reports. Do not regenerate it on every startup. Alternatively, create the file using your password manager with the same permissions. Log in as `operator` at **http://127.0.0.1:3000** and open **FitnessApp → FitnessApp logs** (`/d/fitnessapp-logs/fitnessapp-logs`). The datasource and dashboard are provisioned automatically.

Stop the app with Ctrl+C. Stop monitoring independently:

```bash
docker compose -f monitoring/compose.yaml down
```

`down` preserves named volumes and logs; do not add `--volumes` unless intentionally deleting monitoring history and Grafana state. Grafana reads the bootstrap credential on first initialization only; changing its file does not rotate an existing database password. Rotate through Grafana account settings and your password manager. Losing the credential requires Grafana's documented admin-password recovery procedure, not deleting application data.

To run without forwarding, omit the environment variable:

```bash
dotnet run --project src/FitnessApp.Server
```

`Logging:Dashboard:Enabled` defaults to `false`; use `Logging__Dashboard__Enabled=true` or `dotnet user-secrets set "Logging:Dashboard:Enabled" "true" --project src/FitnessApp.Server` and restart to enable. Remove that User Secret or set it to false to disable. Disabling stops new spool writes; it does not delete retained files or stop Alloy from reading old ones. Information events remain available in the dashboard to follow requests. Once the spool file is ready, console output starts at Warning by default. Before initialization succeeds, events remain on the console. Configure `Logging:Dashboard:ConsoleMinimumLevel` (environment variable `Logging__Dashboard__ConsoleMinimumLevel`) to change only that console threshold. Do not use a Console provider-level filter to silence Information: the shared formatter must still receive those events for the spool. With forwarding disabled, or if the spool fails, rejects an oversized event or has a full queue, the normal bounded console output remains available. Do not attach log files to public PRs.

## Find an exception and its request

1. Start with **Logs**, the first panel on both phone and desktop. Its default range is 48 hours. Choose **Miljø**, **Niveau** and a **Kategori** preset, or enter literal, case-sensitive text in **Søg i logs**. Empty text fields match everything. **Alle logs · nulstil** clears filters while preserving the selected time range; **Kun fejl** selects Error/Critical. The level selector offers the fixed .NET log levels even when none of that level have arrived; an empty error selection must not fall back to All. Category presets filter the structured category without adding index labels; free-text search can find any other category/event name.
2. The counters show matching logs, warnings and errors for the selected period. Successful empty queries show zero, while datasource errors remain errors. Zero errors does not establish that the app or ingestion is healthy: when **Logs i perioden** is zero, clear filters, widen the range to 72 hours and check forwarding. Counters count events, not distinct failures.
3. Expand **Undtagelser og udvikling over tid** for exceptions and the warning/error timeline. Use a line's **⋮ → Show log details** menu for its timestamp and the **Log line** section containing all original JSON fields, exception type, HResult, stack frames and nested details. The compact message display hides timestamps in the main list to leave room for messages on phones; it does not replace the stored JSON. Enable timestamps through the log view's controls when needed. Choose **Følg request** to reopen all categories/severities for that request; the link clears text filters and covers the retained 72 hours.
4. Alternatively paste `traceId` into **Request-ID**, clear the other filters and inspect **Logs**. This filter matches the original structured line; empty input and the legacy `.*` value both show all requests. The app's generated IDs can be pasted directly; advanced regular expressions are also supported. Use the log view's sorting control to read a flow from oldest to newest. A failed API's safe ProblemDetails response also includes this ID. Request summaries contain status, duration and route **template**, never URL/query values.
5. For advanced queries, open Explore with **FitnessApp Loki**, retaining the time range, and use LogQL, for example:

```logql
{application="FitnessApp", environment="Development"} | json | traceId="synthetic-request-id"
{application="FitnessApp", severity=~"Warning|Error|Critical"} | json | category="UnhandledException"
{application="FitnessApp"} |= "NotificationProcessingFailed"
```

No trace/request ID or category is an index label. JSON is retained intact for query-time extraction. Missing request IDs are normal for startup/background events. This correlates local requests; it does not introduce distributed tracing.

## Read the overview and request analysis

- **Seneste app-log** shows the newest event timestamp in the selected period, using only the environment filter. A backward log query fetches at most one event; its timestamp is shown directly, without a fixed metric lookback that could exceed retention for historical periods. This is not a heartbeat or proof of ingestion health; a quiet application produces no new events. Missing data stays missing rather than becoming a current timestamp or a successful zero. Keep query ranges within the retained 72 hours. A stale absolute range may exceed Loki’s lookback bound; if panel status reports `past now - max_query_lookback`, choose the last 48 hours ending now. Query failures remain errors, not successful zero counts.
- **HTTP-svar over tid** counts `RequestCompleted` events by status code. **Svartider** shows median/p95 duration in milliseconds. These panels and **Hyppigste HTTP-problemer** use only environment, request ID and time; severity/category/text filters intentionally do not hide Information-level request summaries. A 404 or 500 may have Information severity; HTTP status and logging severity answer different questions. Percentiles from a few requests are not reliable performance estimates. No samples means no latency data, not zero milliseconds.
- Click a status series or HTTP-status cell to reopen logs with the same status; the link preserves environment, request ID and time while resetting unrelated filters. The top HTTP-problem list groups 4xx/5xx by safe route template and status; its link shows that status across endpoints. Unknown routes intentionally share a fallback template: raw URLs/query strings are not logged.
- **Hyppigste fejl i koden** counts matching Error/Critical events by category and exception type, using all log filters. Counts are events, not distinct failures. **Undtagelser og kodesteder** shows the exception type plus `exception.source.location` and method, where available; expand log details for original JSON and the complete nested stack traces.
- A source location is the first relevant application frame on that exception, not necessarily the root cause; inspect inner exceptions too. The formatter resolves frames only for newly reported exceptions, never for every Information event. The source fields include only a project-relative `src/FitnessApp.../*.cs` path, line and method. Without matching PDB symbols, file/line can be unavailable; the application method is retained when discoverable. No path/line is guessed from a category. Older stored events do not gain source fields retroactively.
- Logs are explicit application/framework logger calls, not automatic records of every executed line. The shared request middleware emits one completion summary per API request; a source location for that logging call would describe the middleware, not diagnose a 404. Business/service events keep their own categories and can be correlated within a request using its ID. The pipeline does not add browser telemetry or cross-request tracing.

## Pipeline, noise and privacy

The existing `Microsoft.Extensions.Logging` framework owns levels and events. Its one console formatter serializes a JSON line once and optionally enqueues it to a small rotating spool. Console-only filtering then keeps routine Information events out of the console while forwarding; warnings/errors still appear, and rejected spool events fall back to console. Alloy's supported `loki.source.file` → `loki.process` → `loki.write` pipeline tails the same JSON, sets timestamps and indexes only `application`, `environment` and `severity`. No Docker socket, app network calls, second logging framework or NuGet dependency is needed.

The primary noise source was EF Core's Information-level SQL commands, particularly the notification worker polling every 30 seconds. Command/EF infrastructure and HTTP-client categories now start at Warning; ASP.NET Core remains Warning. One application start/stop event replaces verbose lifetime output, and each API request has one concise completion summary. Useful application/audit events and warnings/errors remain enabled. The exception handler owns handled-error logging and suppresses ASP.NET Core's duplicate diagnostic for those handled failures. The formatter retains details once per exception object (weak references); later events carry `exceptionLoggedPreviously=true`, and wrappers reference an already reported inner exception. Additional stack frames acquired during propagation are retained in `additionalExceptionStack` / `additionalStackTrace`. EF errors remain visible even when existing services translate a database failure into a conflict response. Use the shared request ID to follow the operation; event counts are not distinct-request counts.

Only explicitly allowed operational properties are serialized. Arbitrary scopes/state, SQL, request URLs, headers, bodies, user/registration IDs, tokens, cookies and health/nutrition/diary values are excluded. Application message templates are developer-authored constants; values outside the allowlist render as `[redacted]`. Framework messages use their event name because their formatted text can contain SQL and URLs even with EF sensitive-data logging disabled. Raw exception **messages** and `Data` can contain secrets and are excluded; type, HResult, stack trace and nested/aggregate types/stacks remain. This deliberate privacy boundary applies equally to console and files. It is not regex-based universal secret scrubbing: keep templates constant and never put private values into allowlisted fields. EF sensitive-data/detailed-error logging remains disabled.

## Bounds, outages and retention

- The optional spool has a nonblocking 256-entry queue, at most 64 KiB per queued line, and four files of at most 5 MiB each in `src/FitnessApp.Server/App_Data/logs`. On restart it appends to the most recently modified file; rotation reuses a missing slot or the oldest other file. Names are reused across restarts. Queue overflow, oversized file events, rotation while Alloy is down and immediate shutdown can lose dashboard events; rejected entries remain on the bounded console. This is diagnostic logging, not an audit ledger.
- File I/O runs on a background worker, outside requests/startup. A file failure disables spooling until restart and emits one safe warning; accepted entries not successfully flushed are replayed to standard error only if their console copy was suppressed. The same best-effort drain runs on graceful cancellation. Console logging continues; an interrupted write may leave a partial file line or duplicate the in-flight event. Abrupt process termination can still lose buffered events. No app retries or HTTP connections depend on Loki. The console queue is also bounded and drops on overflow.
- Alloy persists offsets in `alloy-data`, with 256 KiB batches, 3-second remote timeout and three bounded retries. No WAL or unbounded retry spool. Its error diagnostics go to Docker's bounded logs, not the application's console. Replays after offset loss may duplicate entries; files older than Loki's acceptance window or rotated away cannot be recovered.
- Loki uses persistent `loki-data`, TSDB/filesystem storage and a compactor with **72-hour retention**, a one-hour deletion delay, 0.02 MiB/s accepted ingestion and a 1 MiB burst. Query cache is 32 MiB; parallelism and stream counts are capped. Allow for compaction/cleanup lag and filesystem/index/WAL overhead. Retention and rate limits bound normal growth (about 5.1 GiB of uncompressed accepted lines per 72 hours), **not a hard filesystem quota**. Docker named volumes have no portable byte quota: reserve and monitor disk space; a future hosted deployment needs an explicit filesystem/VM quota and disk alerts. A full monitoring disk must not affect application requests; keep production monitoring storage separate from application data.
- `grafana-data` persists the admin account and preferences; `alloy-data` persists a fixed set of file offsets. Each container's own console logs rotate at 2 × 5 MiB. Memory/CPU ceilings are Loki 512 MiB/1 CPU, Alloy 192 MiB/0.5 CPU and Grafana 256 MiB/0.5 CPU. Increase deliberately if manual testing shows a need.

App spool permissions are owner-only on Unix, outside `wwwroot`. Alloy runs as the image's default root user to read this read-only private mount, with no Docker socket or host write mount. Protect Docker access, volumes, backups and the local OS account. Stop Alloy and remove only `App_Data/logs/fitness-*.jsonl` when intentionally purging local spool history; removing Loki/Grafana volumes is a separate destructive operation.

## Validation and future hosted deployment

Static validation without starting any services:

```bash
docker compose -f monitoring/compose.yaml config --quiet
python3 -m json.tool monitoring/grafana/dashboards/fitnessapp.json > /dev/null
dotnet build src/FitnessApp.Server/FitnessApp.Server.csproj --no-restore --disable-build-servers --verbosity minimal -m:1 -p:BuildInParallel=false
```

Loki `3.7.8`, Alloy `v1.20.1` and Grafana `13.2.4` are pinned stable releases. Their local native binaries can additionally validate configuration with `loki -config.file=monitoring/loki.yaml -verify-config=true` and `alloy validate monitoring/config.alloy`; these commands exit without starting a service. See the official [Alloy file source](https://grafana.com/docs/alloy/latest/reference/components/loki/loki.source.file/), [Loki retention](https://grafana.com/docs/loki/latest/operations/storage/retention/) and [Grafana provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/) references. Recheck compatible patched versions before upgrades.

This configuration is for a trusted local workstation: Grafana binds only `127.0.0.1`; Loki and Alloy have no published ports and use an isolated internal network. Loki has no authentication and must never be published or routed publicly. This does not deploy the app or implement the future mini-PC/VM plan. Hosted use needs separate reviewed storage quotas, TLS/private access, identity/access restrictions, credential lifecycle and backup/restore. Local Compose secrets are mounted files, not an encrypted secret manager.

Manual verification (use synthetic data):

- Run once with forwarding disabled, then enabled; verify normal Information console output when disabled, Information request events in Grafana when enabled, and console warnings/errors in both modes. Verify the full-queue/oversized-event and file-failure console fallbacks without logging private data.
- On mobile and desktop, verify the first log list, literal text/request search, category/level filters, reset/error shortcuts, zero counters and expanded exception/timeline row. Check that a datasource failure remains visible rather than becoming a successful zero count. Confirm the latest-log timestamp, status graphs, median/p95, top lists and status-to-log links; change severity/category/text filters and confirm the documented request-panel independence.
- Confirm dashboard/datasource provisioning, environment/severity/category filters and stack traces. Trigger a controlled failure with and without matching PDB symbols; verify a project-relative file/line or method-only fallback, inner exception details, and no absolute source path in the new source fields; follow its request-ID link and verify related summary/application events and one boundary exception.
- Stop Loki/Alloy and continue requests; verify responsiveness, bounded spool/queue and absence of retry noise in the app console. Restart and check recent retained events and persisted Grafana account/offsets.
- Check logs contain no synthetic sentinel passwords/tokens/cookies/header/body/health values, including an exception whose message contains a sentinel. Check the actual Loki stream labels exclude trace IDs and category.
- Verify localhost-only access, container secret readability, rotation, retention cleanup, resource limits and shutdown persistence. No runtime behavior is established by static validation or a successful build.
