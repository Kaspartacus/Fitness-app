using FitnessApp.Infrastructure.Nutrition;

namespace FitnessApp.Server.Nutrition;

public static class FridaImportCommand
{
    public static async Task<int> RunAsync(WebApplication app, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2 || args[0] != "import-frida")
        {
            Console.Error.WriteLine("Brug: dotnet run --project src/FitnessApp.Server -- import-frida <sti-til-Frida_5.5_Dataset.xlsx>");
            return 2;
        }
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<FridaImportService>().ImportAsync(args[1], cancellationToken);
        Console.WriteLine(result.Succeeded ? $"{result.Message} Fødevarer: {result.FoodCount}." : result.Message);
        return result.Succeeded ? 0 : 1;
    }
}
