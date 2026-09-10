using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FitnessApp.Infrastructure.Persistence;

public sealed class FitnessDbContextFactory : IDesignTimeDbContextFactory<FitnessDbContext>
{
    public FitnessDbContext CreateDbContext(string[] args)
    {
        var contentRoot = ResolveContentRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        var options = new DbContextOptionsBuilder<FitnessDbContext>()
            .UseSqlite(SqliteConnectionString.Resolve(connectionString, contentRoot))
            .Options;

        return new FitnessDbContext(options);
    }

    private static string ResolveContentRoot()
    {
        for (var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory()); currentDirectory is not null;
             currentDirectory = currentDirectory.Parent)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "appsettings.json")))
            {
                return currentDirectory.FullName;
            }

            var serverUnderSource = Path.Combine(currentDirectory.FullName, "src", "FitnessApp.Server");
            if (File.Exists(Path.Combine(serverUnderSource, "appsettings.json")))
            {
                return serverUnderSource;
            }

            var siblingServer = Path.Combine(currentDirectory.FullName, "FitnessApp.Server");
            if (File.Exists(Path.Combine(siblingServer, "appsettings.json")))
            {
                return siblingServer;
            }
        }

        throw new DirectoryNotFoundException("Could not find the FitnessApp.Server content root.");
    }
}
