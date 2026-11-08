using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Server.Staging;

public static class StagingOperations
{
    // A command mode in the existing host, not a second service. No hosted workers start.
    public static async Task<int> MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FitnessDbContext>().Database.MigrateAsync(cancellationToken);
            return 0;
        }
        catch
        {
            // Provider messages can include private paths/data. Deployment reports only the exit code.
            return 1;
        }
    }

    public static async Task<int> ProbeAsync()
    {
        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch { return 1; }
    }

    public static IApplicationBuilder UseStagingHealth(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path != "/health/live" && context.Request.Path != "/health/ready")
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        var ready = true;
        if (context.Request.Path == "/health/ready")
        {
            try
            {
                var database = context.RequestServices.GetRequiredService<FitnessDbContext>().Database;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                ready = await database.CanConnectAsync(timeout.Token) &&
                    !(await database.GetPendingMigrationsAsync(timeout.Token)).Any();
            }
            catch { ready = false; }
        }
        context.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync(ready ? "Healthy" : "Unhealthy", context.RequestAborted);
    });
}
