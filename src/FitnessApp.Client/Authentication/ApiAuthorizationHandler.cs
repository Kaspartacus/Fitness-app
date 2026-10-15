using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace FitnessApp.Client.Authentication;

public sealed class ApiAuthorizationHandler(
    InMemoryAuthenticationStateProvider authenticationState,
    NavigationManager navigationManager,
    AuthenticationClient? authenticationClient = null) : DelegatingHandler
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

        var generation = authenticationState.Generation;
        if (isApplicationApi && authenticationClient is not null && authenticationState.AccessToken is not null &&
            authenticationState.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            var restored = await authenticationClient.RefreshAsync(cancellationToken: cancellationToken);
            if (restored == CurrentUserStatus.Unavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (isApplicationApi && authenticationState.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                authenticationState.AccessToken);
        }

        if (isApplicationApi && generation != authenticationState.Generation)
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        var response = await base.SendAsync(request, cancellationToken);
        if (isApplicationApi && generation != authenticationState.Generation)
        {
            response.Dispose();
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        }
        if (isApplicationApi && authenticationClient is not null && response.StatusCode == HttpStatusCode.Unauthorized &&
            requestUri?.AbsolutePath is not "/api/auth/login")
        {
            var restored = await authenticationClient.RefreshAsync(cancellationToken: cancellationToken);
            if (restored == CurrentUserStatus.Unavailable)
            {
                response.Dispose();
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            if (restored == CurrentUserStatus.Succeeded)
            {
                using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
                foreach (var header in request.Headers) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authenticationState.AccessToken);
                if (request.Content is not null)
                {
                    retry.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                    foreach (var header in request.Content.Headers) retry.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                response.Dispose();
                if (generation != authenticationState.Generation) return new HttpResponseMessage(HttpStatusCode.Conflict);
                response = await base.SendAsync(retry, cancellationToken);
                if (generation != authenticationState.Generation)
                {
                    response.Dispose();
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }
            }
        }
        if (isApplicationApi &&
            response.StatusCode is HttpStatusCode.Unauthorized &&
            requestUri?.AbsolutePath is not "/api/auth/login")
        {
            authenticationState.ClearSession();
            if (!string.Equals(new Uri(navigationManager.Uri).AbsolutePath.TrimEnd('/'), "/login", StringComparison.OrdinalIgnoreCase))
            {
                navigationManager.NavigateTo(SafeReturnPath.LoginUrl(navigationManager.Uri), replace: true);
            }
        }

        return response;
    }
}
