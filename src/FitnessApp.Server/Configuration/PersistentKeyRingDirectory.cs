namespace FitnessApp.Server.Configuration;

// Resolve lazily from the completed host's configuration. Test/deployment configuration
// sources added during host composition must choose both validation and actual key storage.
internal sealed class PersistentKeyRingDirectory
{
    public DirectoryInfo Location { get; }

    public PersistentKeyRingDirectory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["DataProtection:KeyRingPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("DataProtection:KeyRingPath is required.");
        var path = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath : Path.Combine(environment.ContentRootPath, configuredPath));
        var applicationDirectory = new DirectoryInfo(environment.ContentRootPath);
        for (var directory = applicationDirectory; directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            { applicationDirectory = directory; break; }
        }
        var prefix = applicationDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.Equals(applicationDirectory.FullName, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DataProtection:KeyRingPath must be a persistent private directory outside the repository/application. Move the existing key ring with an operator-managed backup; do not replace it.");
        // Validate the location before creating or changing permissions on any directory.
        Location = new DirectoryInfo(PrivateDirectory.ResolveAndCreate(path, environment.ContentRootPath,
            environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "DataProtection:KeyRingPath"));
    }
}
