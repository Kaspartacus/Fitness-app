using System.Threading.RateLimiting;
using FitnessApp.Application.Authentication;
using FitnessApp.Infrastructure;
using FitnessApp.Infrastructure.Persistence;
using FitnessApp.Server.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var bootstrapAdministrator = args.Length == 1 && args[0] == "bootstrap-admin";
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
var connectionString = SqliteConnectionString.Resolve(
    configuredConnectionString,
    builder.Environment.ContentRootPath);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(connectionString);
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") ||
        context.Request.Path.StartsWithSegments("/api/registrations") ||
        context.Request.Path.StartsWithSegments("/api/admin"))
    {
        AuthenticationHttpResponses.SetNoStore(context.Response);
    }

    await next(context);
});

app.MapAuthenticationEndpoints();
app.MapRegistrationEndpoints();
app.MapUserAdministrationEndpoints();

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
