using FitnessApp.Application.Authentication;
using Microsoft.Extensions.Options;

namespace FitnessApp.Server.Authentication;

internal static class RefreshCookie
{
    // __Secure- requires HTTPS; no Domain attribute, and the narrow path excludes normal API calls.
    internal const string Name = "__Secure-FitnessApp.Refresh";
    private const string Path = "/api/auth";

    internal static void Write(HttpResponse response, RefreshCredential credential) =>
        response.Cookies.Append(Name, credential.Value, Options(credential.ExpiresAt));

    internal static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict,
        Path = Path, IsEssential = true, Expires = expires
    };

    internal static bool IsTrusted(HttpRequest request, IOptions<PublicAppOptions> options)
    {
        if (request.Headers["X-FitnessApp-Session"] != "1") return false;
        var trusted = new Uri(options.Value.BaseUrl).GetLeftPart(UriPartial.Authority);
        // Never trust the request Host to define the allowed origin. Reject absent/null/multiple origins.
        if (request.Headers.Origin.Count != 1 || request.Headers.Origin[0] != trusted) return false;
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        return fetchSite.Length == 0 || fetchSite == "same-origin";
    }
}
