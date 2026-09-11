using System.Threading.RateLimiting;
using FitnessApp.Server.Running;
using FitnessApp.Server.Strength;
using FitnessApp.Application.Authentication;
using FitnessApp.Infrastructure;
using FitnessApp.Infrastructure.Persistence;
using FitnessApp.Server.Authentication;
using FitnessApp.Server.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var bootstrapAdministrator = args.Length == 1 && args[0] == "bootstrap-admin";
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

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

var dataProtectionKeyRingPath = PrivateDirectory.ResolveAndCreate(
    builder.Configuration["DataProtection:KeyRingPath"],
    builder.Environment.ContentRootPath,
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
    "DataProtection:KeyRingPath");
builder.Services.AddDataProtection()
    .SetApplicationName("FitnessApp")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));

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

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
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
        Title = "Der opstod en uventet fejl."
    });
}));

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
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
        context.Request.Path.StartsWithSegments("/api/running"))
    {
        AuthenticationHttpResponses.SetNoStore(context.Response);
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
