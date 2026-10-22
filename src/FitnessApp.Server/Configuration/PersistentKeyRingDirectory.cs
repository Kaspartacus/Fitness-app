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
        var contentRoot = CanonicalDirectoryPath(environment.ContentRootPath);
        var path = CanonicalDirectoryPath(Path.IsPathRooted(configuredPath)
            ? configuredPath : Path.Combine(contentRoot, configuredPath));
        var webRoot = CanonicalDirectoryPath(environment.WebRootPath ?? Path.Combine(contentRoot, "wwwroot"));
        var applicationDirectory = new DirectoryInfo(contentRoot);
        for (var directory = applicationDirectory; directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            { applicationDirectory = directory; break; }
        }
        if (IsWithin(path, CanonicalDirectoryPath(applicationDirectory.FullName)))
            throw new InvalidOperationException("DataProtection:KeyRingPath must be a persistent private directory outside the repository/application. Move the existing key ring with an operator-managed backup; do not replace it.");
        if (IsWithin(path, webRoot))
            throw new InvalidOperationException("DataProtection:KeyRingPath must be outside the public web root.");
        // Validate actual targets before creation. Persist using the resolved path, not an alias
        // which could redirect the key repository back into the application or public web root.
        Location = new DirectoryInfo(PrivateDirectory.ResolveAndCreate(path, contentRoot, webRoot, "DataProtection:KeyRingPath"));
    }

    private static bool IsWithin(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(directory, comparison) || path.StartsWith(
            directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }

    private static string CanonicalDirectoryPath(string path, int remainingLinks = 64)
    {
        var fullPath = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(fullPath)!;
        foreach (var segment in fullPath[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(resolved, segment);
            var directory = new DirectoryInfo(candidate);
            if (directory.LinkTarget is not null)
            {
                if (remainingLinks == 0)
                    throw new InvalidOperationException("The key storage directory has too many symbolic links.");
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !Directory.Exists(target.FullName))
                    throw new InvalidOperationException("The key storage directory contains an unresolved symbolic link.");
                // Targets can themselves have aliased parents (for example /tmp or /var on macOS).
                resolved = CanonicalDirectoryPath(target.FullName, remainingLinks - 1);
            }
            else
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("The key storage directory contains an unsupported reparse point.");
                if (!directory.Exists && File.Exists(candidate))
                    throw new InvalidOperationException("The key storage path must contain directories only.");
                // A missing suffix is safe to append after all its existing ancestors were resolved.
                resolved = candidate;
            }
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }
}
