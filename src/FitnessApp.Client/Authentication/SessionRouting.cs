namespace FitnessApp.Client.Authentication;

public static class SessionRouting
{
    public static bool ShowSessionGate(bool sessionPending, string uri) =>
        sessionPending && !IsPublicRecoveryRoute(uri);

    private static bool IsPublicRecoveryRoute(string uri)
    {
        var path = new Uri(uri, UriKind.Absolute).AbsolutePath.TrimEnd('/');
        return path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/registrer", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/glemt-adgangskode", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/nulstil-adgangskode", StringComparison.OrdinalIgnoreCase);
    }
}
