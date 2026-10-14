namespace FitnessApp.Client.Authentication;

public static class SafeReturnPath
{
    private static readonly Uri LocalOrigin = new("https://fitness.invalid/");

    public static string Validate(string? path)
    {
        if (path is not { Length: > 0 and <= 1024 } || !IsLocalPath(path)) return "/";
        // Decode once, allowing UTF-8 route names such as træninger. Encoded URI delimiters,
        // percent signs and control bytes are never accepted, including double encoding.
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] != '%') continue;
            if (i + 2 >= path.Length || !byte.TryParse(path.AsSpan(i + 1, 2),
                System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                value <= 32 || value == 127 || value is (byte)'/' or (byte)'\\' or (byte)'?' or (byte)'#' or (byte)'%') return "/";
            i += 2;
        }
        var decoded = Uri.UnescapeDataString(path);
        if (!IsLocalPath(decoded) || decoded.Contains('%') ||
            !Uri.TryCreate(LocalOrigin, decoded, out var destination) || destination.GetLeftPart(UriPartial.Authority) != LocalOrigin.GetLeftPart(UriPartial.Authority) ||
            destination.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase)) return "/";
        // URI normalization removes dot segments and emits one canonical escaped local path.
        var canonical = destination.PathAndQuery;
        return IsLocalPath(canonical) ? canonical : "/";
    }

    private static bool IsLocalPath(string path) => path.StartsWith('/') &&
        !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Contains('#') && !path.Any(char.IsControl);

    public static string LoginUrl(string absoluteUri) => "/login?returnUrl=" + Uri.EscapeDataString(Validate(new Uri(absoluteUri).PathAndQuery));
}
