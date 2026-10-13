namespace FitnessApp.Client.Authentication;

public static class SafeReturnPath
{
    public static string Validate(string? path) => path is { Length: > 0 and <= 1024 } && path.StartsWith('/') &&
        !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Contains('%') && !path.Any(char.IsControl) &&
        !path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ? path : "/";
    public static string LoginUrl(string absoluteUri) => "/login?returnUrl=" + Uri.EscapeDataString(Validate(new Uri(absoluteUri).PathAndQuery));
}
