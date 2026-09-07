using System.Net;
using System.Net.Http.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components;

namespace FitnessApp.IntegrationTests;

public sealed class ClientAuthenticationTests
{
    [Theory]
    [InlineData(HttpStatusCode.NoContent, LogoutStatus.Succeeded, "/login")]
    [InlineData(HttpStatusCode.Unauthorized, LogoutStatus.SessionRejected, "/login")]
    [InlineData(HttpStatusCode.InternalServerError, LogoutStatus.ServerLogoutUnconfirmed, "/login?logout=unconfirmed")]
    public async Task LogoutHandlesHttpOutcomes(
        HttpStatusCode responseStatus,
        LogoutStatus expectedStatus,
        string expectedPath)
    {
        using var fixture = CreateFixture(_ => Task.FromResult(new HttpResponseMessage(responseStatus)));

        var result = await fixture.Client.LogoutAsync();

        Assert.Equal(expectedStatus, result);
        Assert.Null(fixture.AuthenticationState.AccessToken);
        Assert.Equal(new Uri(new Uri(fixture.Navigation.BaseUri), expectedPath).ToString(), fixture.Navigation.Uri);
    }

    [Fact]
    public async Task LogoutHandlesNetworkFailureLocally()
    {
        using var fixture = CreateFixture(_ => throw new HttpRequestException("Test network failure."));

        var result = await fixture.Client.LogoutAsync();

        Assert.Equal(LogoutStatus.ServerLogoutUnconfirmed, result);
        Assert.Null(fixture.AuthenticationState.AccessToken);
        Assert.Equal("https://fitness.test/login?logout=unconfirmed", fixture.Navigation.Uri);
    }

    [Fact]
    public async Task CurrentUserCanRetryAfterRecoverableFailure()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.InternalServerError),
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AuthenticatedUserResponse(
                    "admin@example.test",
                    "Administrator",
                    ["Admin"]))
            }
        ]);
        using var fixture = CreateFixture(_ => Task.FromResult(responses.Dequeue()));

        var failed = await fixture.Client.GetCurrentUserAsync();
        var retried = await fixture.Client.GetCurrentUserAsync();

        Assert.Equal(CurrentUserStatus.Unavailable, failed.Status);
        Assert.Equal(CurrentUserStatus.Succeeded, retried.Status);
        Assert.Equal("Administrator", retried.User?.DisplayName);
    }

    [Fact]
    public async Task OwnProtectedApiUnauthorizedClearsSession()
    {
        var authenticationState = CreateAuthenticatedState();
        var navigation = new TestNavigationManager();
        using var handler = new ApiAuthorizationHandler(authenticationState, navigation)
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };

        using var response = await client.GetAsync("api/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(authenticationState.AccessToken);
        Assert.Equal("https://fitness.test/login", navigation.Uri);
    }

    [Fact]
    public async Task ExternalUnauthorizedDoesNotClearSessionOrReceiveToken()
    {
        string? authorization = null;
        var authenticationState = CreateAuthenticatedState();
        var navigation = new TestNavigationManager();
        using var handler = new ApiAuthorizationHandler(authenticationState, navigation)
        {
            InnerHandler = new StubHttpMessageHandler(request =>
            {
                authorization = request.Headers.Authorization?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            })
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://external.example/api/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("test-token", authenticationState.AccessToken);
        Assert.Equal("https://fitness.test/", navigation.Uri);
        Assert.Null(authorization);
    }

    [Fact]
    public async Task LoginUnauthorizedRemainsAFormError()
    {
        using var fixture = CreateFixture(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var error = await fixture.Client.LoginAsync(new LoginRequest
        {
            Email = "admin@example.test",
            Password = "invalid"
        });

        Assert.Equal("E-mail eller adgangskode er forkert, eller kontoen har ikke adgang.", error);
        Assert.Equal("test-token", fixture.AuthenticationState.AccessToken);
        Assert.Equal("https://fitness.test/", fixture.Navigation.Uri);
    }

    private static ClientFixture CreateFixture(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
    {
        var authenticationState = CreateAuthenticatedState();
        var navigation = new TestNavigationManager();
        var httpClient = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri(navigation.BaseUri)
        };
        var factory = new TestHttpClientFactory(httpClient);
        return new ClientFixture(
            new AuthenticationClient(factory, authenticationState, navigation),
            authenticationState,
            navigation,
            httpClient);
    }

    private static InMemoryAuthenticationStateProvider CreateAuthenticatedState()
    {
        var authenticationState = new InMemoryAuthenticationStateProvider();
        authenticationState.SetSession(new LoginResponse(
            "test-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            new AuthenticatedUserResponse("admin@example.test", "Administrator", ["Admin"])));
        return authenticationState;
    }

    private sealed record ClientFixture(
        AuthenticationClient Client,
        InMemoryAuthenticationStateProvider AuthenticationState,
        TestNavigationManager Navigation,
        HttpClient HttpClient) : IDisposable
    {
        public void Dispose() => HttpClient.Dispose();
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://fitness.test/", "https://fitness.test/");

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            Uri = ToAbsoluteUri(uri).ToString();

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Uri = ToAbsoluteUri(uri).ToString();
    }
}
