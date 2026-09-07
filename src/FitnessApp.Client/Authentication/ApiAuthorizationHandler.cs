using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace FitnessApp.Client.Authentication;

public sealed class ApiAuthorizationHandler(
    InMemoryAuthenticationStateProvider authenticationState,
    NavigationManager navigationManager) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri;
        var appBaseUri = new Uri(navigationManager.BaseUri);
        var isApplicationApi = requestUri is not null &&
            requestUri.IsAbsoluteUri &&
            requestUri.Scheme == appBaseUri.Scheme &&
            requestUri.Authority == appBaseUri.Authority &&
            requestUri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);

        if (isApplicationApi && authenticationState.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                authenticationState.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (isApplicationApi &&
            response.StatusCode is HttpStatusCode.Unauthorized &&
            requestUri?.AbsolutePath is not "/api/auth/login")
        {
            authenticationState.ClearSession();
            if (!navigationManager.Uri.EndsWith("/login", StringComparison.Ordinal))
            {
                navigationManager.NavigateTo("/login", replace: true);
            }
        }

        return response;
    }
}
