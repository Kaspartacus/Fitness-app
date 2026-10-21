using FitnessApp.Server.Diary;
using System.Threading.RateLimiting;
using System.Diagnostics;
using FitnessApp.Server.Logging;
using Microsoft.Extensions.Logging.Console;
using System.IdentityModel.Tokens.Jwt;
using FitnessApp.Server.Notifications;
using FitnessApp.Infrastructure.Notifications;
using FitnessApp.Server.Calendar;
using FitnessApp.Server.Running;
using FitnessApp.Server.Settings;
using FitnessApp.Server.Strength;
using FitnessApp.Server.Nutrition;
using FitnessApp.Application.Authentication;
using FitnessApp.Infrastructure;
using FitnessApp.Infrastructure.Persistence;
using FitnessApp.Server.Authentication;
using FitnessApp.Server.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var bootstrapAdministrator = args.Length == 1 && args[0] == "bootstrap-admin";
var importFrida = args.Length > 0 && args[0] == "import-frida";
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSingleton<LogSpool>();
builder.Services.AddHostedService(services => services.GetRequiredService<LogSpool>());
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "fitness-json";
    options.MaxQueueLength = 256;
    options.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite;
});
builder.Logging.AddConsoleFormatter<StructuredConsoleFormatter, ConsoleFormatterOptions>();

builder.Services.AddSingleton<IValidateOptions<PasswordResetOptions>, PasswordResetOptionsValidator>();
builder.Services.AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<PublicAppOptions>, PublicAppOptionsValidator>();
builder.Services.AddOptions<PublicAppOptions>()
    .Bind(builder.Configuration.GetSection(PublicAppOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<FitnessApp.Infrastructure.Email.EmailDeliveryOptions>, EmailDeliveryOptionsValidator>();
builder.Services.AddOptions<FitnessApp.Infrastructure.Email.EmailDeliveryOptions>()
    .Bind(builder.Configuration.GetSection(FitnessApp.Infrastructure.Email.EmailDeliveryOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            options.PickupDirectory = Path.GetFullPath(Path.IsPathRooted(options.PickupDirectory)
                ? options.PickupDirectory
                : Path.Combine(builder.Environment.ContentRootPath, options.PickupDirectory));
        }
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FitnessApp.Infrastructure.Email.SmtpOptions>, SmtpOptionsValidator>();
builder.Services.AddOptions<FitnessApp.Infrastructure.Email.SmtpOptions>()
    .Bind(builder.Configuration.GetSection(FitnessApp.Infrastructure.Email.SmtpOptions.SectionName))
    .ValidateOnStart();

// Key storage is configured from the final service provider, after host configuration callbacks.
// One resolved directory is shared by Data Protection and the startup availability check.
builder.Services.AddSingleton<PersistentKeyRingDirectory>();
builder.Services.AddDataProtection().SetApplicationName("FitnessApp");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<PersistentKeyRingDirectory, ILoggerFactory>((options, directory, loggers) =>
        options.XmlRepository = new FileSystemXmlRepository(directory.Location, loggers));

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
var connectionString = SqliteConnectionString.Resolve(
    configuredConnectionString,
    builder.Environment.ContentRootPath);

builder.Services.AddSingleton(TimeProvider.System);
var passwordResetTokenLifetimeMinutes = builder.Configuration.GetValue(
    "Authentication:PasswordReset:TokenLifetimeMinutes",
    60);
var passwordResetTokenLifetime = builder.Environment.IsEnvironment("Testing") &&
    builder.Configuration.GetValue<int?>("Testing:PasswordResetTokenLifetimeMilliseconds") is { } testLifetimeMilliseconds
        ? TimeSpan.FromMilliseconds(testLifetimeMilliseconds)
        : TimeSpan.FromMinutes(passwordResetTokenLifetimeMinutes);
builder.Services.AddInfrastructure(connectionString, passwordResetTokenLifetime);
var emailTransport = builder.Configuration.GetValue("Email:Transport", "Smtp");
builder.Services.AddEmailDelivery(usePickupDirectory: emailTransport is "Pickup");
builder.Services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
builder.Services.AddSingleton<IRefreshCredentialDeriver, RefreshCredentialDeriver>();
builder.Services.AddOptions<WebPushOptions>().Bind(builder.Configuration.GetSection("WebPush"))
    .Validate(o => (!o.Configured && o.PublicKey.Length == 0 && o.PrivateKey.Length == 0 && o.Subject.Length == 0) ||
        (o.Configured && PushEndpointPolicy.ValidKey(o.PublicKey, 65) && PushEndpointPolicy.ValidKey(o.PrivateKey, 32) &&
         Uri.TryCreate(o.Subject, UriKind.Absolute, out var subject) && (subject.Scheme is "mailto" or "https")),
        "WebPush must contain a complete valid VAPID configuration, or be entirely empty.").ValidateOnStart();
builder.Services.AddSingleton<IWebPushSender, WebPushSender>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<NotificationWorker>();

builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>, JwtBearerOptionsSetup>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        AuthenticationConstants.AdminPolicy,
        policy => policy.RequireRole(AuthenticationConstants.AdminRole)));

var loginPermitLimit = builder.Configuration.GetValue("Authentication:LoginRateLimit:PermitLimit", 10);
var loginWindowSeconds = builder.Configuration.GetValue("Authentication:LoginRateLimit:WindowSeconds", 60);
var registrationPermitLimit = builder.Configuration.GetValue("Authentication:RegistrationRateLimit:PermitLimit", 5);
var registrationWindowSeconds = builder.Configuration.GetValue("Authentication:RegistrationRateLimit:WindowSeconds", 300);
var passwordResetPermitLimit = builder.Configuration.GetValue("Authentication:PasswordResetRateLimit:PermitLimit", 5);
var passwordResetWindowSeconds = builder.Configuration.GetValue("Authentication:PasswordResetRateLimit:WindowSeconds", 300);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        AuthenticationHttpResponses.SetNoStore(context.HttpContext.Response);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "For mange forsøg. Prøv igen senere." },
            cancellationToken);
    };
    options.AddPolicy("session-refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("notifications", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("push-test", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = TimeSpan.FromSeconds(loginWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("registration", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = registrationPermitLimit,
                Window = TimeSpan.FromSeconds(registrationWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("password-reset", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = passwordResetPermitLimit,
                Window = TimeSpan.FromSeconds(passwordResetWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddProblemDetails();

var app = builder.Build();

if (bootstrapAdministrator)
{
    Environment.ExitCode = await AdminBootstrapCommand.RunAsync(app, CancellationToken.None);
    return;
}

if (importFrida)
{
    Environment.ExitCode = await FridaImportCommand.RunAsync(app, args, CancellationToken.None);
    return;
}

if (app.Environment.IsDevelopment())
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<FitnessDbContext>();
    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
    if (pendingMigrations.Length > 0)
    {
        app.Logger.LogInformation(
            "Applying {MigrationCount} pending database migrations in Development.",
            pendingMigrations.Length);
        await dbContext.Database.MigrateAsync();
    }
}

// Never silently initialize replacement keys while encrypted diary rows depend on the old ring.
await using (var diaryKeyScope = app.Services.CreateAsyncScope())
{
    var keyDirectory = diaryKeyScope.ServiceProvider.GetRequiredService<PersistentKeyRingDirectory>().Location;
    var diaryDatabase = diaryKeyScope.ServiceProvider.GetRequiredService<FitnessDbContext>();
    if ((await diaryDatabase.Database.GetAppliedMigrationsAsync()).Contains("20261021120000_AddPrivateDiary") &&
        await diaryDatabase.DiaryEntries.AnyAsync() &&
        !keyDirectory.EnumerateFiles("key-*.xml").Any())
        throw new InvalidOperationException("Existing diary entries require the persistent Data Protection key ring. Restore its backup before starting the server.");
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

// Scope surrounds the handler so failures and their completion event share one request ID.
app.Use(async (context, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["TraceId"] = context.TraceIdentifier,
        ["RequestId"] = context.TraceIdentifier
    });
    var started = Stopwatch.GetTimestamp();
    try { await next(context); }
    finally
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            app.Logger.LogInformation(new EventId(5001, "RequestCompleted"),
                "Request completed. Status: {StatusCode}; DurationMs: {DurationMs}; Endpoint: {Endpoint}",
                context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                ((context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint) as RouteEndpoint)
                    ?.RoutePattern.RawText ?? "unmatched");
    }
});

app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation(
    new EventId(5002, "ApplicationStarted"), "Application started."));
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation(
    new EventId(5003, "ApplicationStopping"), "Application stopping."));

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // The handler below owns handled exception logging. Unhandled failures still use
    // framework diagnostics (for example when the response has already started).
    SuppressDiagnosticsCallback = _ => true,
    ExceptionHandler = async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnhandledException")
            .LogError(
                new EventId(5000, "UnhandledServerError"),
                exception,
                "Unexpected server error. TraceId: {TraceId}",
                context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Der opstod en uventet fejl.",
            Extensions = { ["traceId"] = context.TraceIdentifier }
        });
    }
});

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") &&
        !Path.HasExtension(context.Request.Path.Value))
    {
        // The HTML shell references fingerprinted WebAssembly assets. Revalidate it on
        // navigation so a rebuilt client cannot be paired with assets from an older run.
        context.Response.Headers.CacheControl = "no-cache";
    }

    if (context.Request.Path.StartsWithSegments("/api/auth") ||
        context.Request.Path.StartsWithSegments("/api/registrations") ||
        context.Request.Path.StartsWithSegments("/api/admin") ||
        context.Request.Path.StartsWithSegments("/api/strength") ||
        context.Request.Path.StartsWithSegments("/api/running") ||
        context.Request.Path.StartsWithSegments("/api/calendar") ||
        context.Request.Path.StartsWithSegments("/api/nutrition") ||
        context.Request.Path.StartsWithSegments("/api/settings") ||
        context.Request.Path.StartsWithSegments("/api/notifications") ||
        context.Request.Path.StartsWithSegments("/api/diary") ||
        context.Request.Path.StartsWithSegments("/dagbog"))
    {
        AuthenticationHttpResponses.SetNoStore(context.Response);
        if (context.Request.Path.StartsWithSegments("/api/diary") || context.Request.Path.StartsWithSegments("/dagbog"))
            context.Response.Headers.CacheControl = "no-store, private";
    }

    if (context.Request.Path.StartsWithSegments("/glemt-adgangskode") ||
        context.Request.Path.StartsWithSegments("/nulstil-adgangskode"))
    {
        AuthenticationHttpResponses.SetNoStore(context.Response);
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    await next(context);
});

app.MapAuthenticationEndpoints();
app.MapPasswordResetEndpoints();
app.MapRegistrationEndpoints();
app.MapUserAdministrationEndpoints();
app.MapStrengthProgramEndpoints();
app.MapRunningEndpoints();
app.MapCalendarEndpoints();
app.MapNutritionEndpoints();
app.MapSettingsEndpoints();
app.MapNotificationEndpoints();
app.MapDiaryEndpoints();
app.MapStaticAssets();
app.UseEndpoints(_ => { });

if (app.Environment.IsEnvironment("Testing") &&
    app.Configuration.GetValue<bool>("Testing:EnableTestEndpoints"))
{
    app.MapGet("/api/test/admin", () => Results.Ok())
        .RequireAuthorization(AuthenticationConstants.AdminPolicy);
}

app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
