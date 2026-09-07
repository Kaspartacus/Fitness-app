namespace FitnessApp.Server.Authentication;

internal static class AuthenticationHttpResponses
{
    public static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
