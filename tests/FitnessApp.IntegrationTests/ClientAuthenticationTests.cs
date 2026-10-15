using System.Net;
using System.Net.Http.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

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
        Assert.Equal("https://fitness.test/login?returnUrl=%2F", navigation.Uri);
    }

    [Fact]
    public async Task UnauthorizedStrengthDestinationSurvivesLoginEncodingRoundTrip()
    {
        var navigation = new TestNavigationManager();
        navigation.NavigateTo("/styrke/11111111-1111-1111-1111-111111111111/træninger/22222222-2222-2222-2222-222222222222/aktiv?planlagt=2026-10-12");
        using var handler = new ApiAuthorizationHandler(CreateAuthenticatedState(), navigation)
        {
            InnerHandler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };
        using var response = await client.GetAsync("api/notifications/unread");
        var query = new Uri(navigation.Uri).Query;
        Assert.StartsWith("?returnUrl=", query);
        var suppliedReturnUrl = Uri.UnescapeDataString(query["?returnUrl=".Length..]);
        Assert.Equal("/styrke/11111111-1111-1111-1111-111111111111/tr%C3%A6ninger/22222222-2222-2222-2222-222222222222/aktiv?planlagt=2026-10-12",
            SafeReturnPath.Validate(suppliedReturnUrl));
    }

    [Theory]
    [InlineData("/styrke/tr%C3%A6ninger", "/styrke/tr%C3%A6ninger")]
    [InlineData("/styrke/træninger", "/styrke/tr%C3%A6ninger")]
    [InlineData("/kost?dato=2026-10-%31%32", "/kost?dato=2026-10-12")]
    [InlineData("/%2f%2fevil.test", "/")]
    [InlineData("/%5cevil.test", "/")]
    [InlineData("/%252fevil.test", "/")]
    [InlineData("/%3freturnUrl=external", "/")]
    [InlineData("/%23external", "/")]
    [InlineData("/%0dexternal", "/")]
    [InlineData("/%C2%85external", "/")]
    [InlineData("/%", "/")]
    [InlineData("/%C3", "/")]
    [InlineData("/%2e%2e/login", "/")]
    [InlineData("/a/..//evil.test", "/")]
    public void ReturnPathCanonicalizesUnicodeWithoutAcceptingEncodedRedirects(string input, string expected) =>
        Assert.Equal(expected, SafeReturnPath.Validate(input));

    [Fact]
    public async Task LaterUnauthorizedResponsePreservesNotificationLoginDestination()
    {
        var state = CreateAuthenticatedState();
        var navigation = new TestNavigationManager();
        var destination = "/notifikationer/11111111-1111-1111-1111-111111111111";
        navigation.NavigateTo(destination);
        using var handler = new ApiAuthorizationHandler(state, navigation)
        {
            InnerHandler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };

        using var first = await client.GetAsync("api/notifications/unread");
        var loginDestination = navigation.Uri;
        Assert.Equal("https://fitness.test/login?returnUrl=" + Uri.EscapeDataString(destination), loginDestination);
        using var later = await client.GetAsync("api/notifications");
        Assert.Equal(loginDestination, navigation.Uri);
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

    [Fact]
    public async Task RestorationWaitsForServerAndCanRecoverFromTemporaryFailure()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.ServiceUnavailable),
            new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginResponse("restored-token",
                DateTimeOffset.UtcNow.AddMinutes(15), new("restored@example.test", "Gendannet", ["User"]), "device-session")) }
        ]);
        using var fixture = CreateFixture(_ => Task.FromResult(responses.Dequeue()));
        fixture.AuthenticationState.ClearSession();
        await fixture.Client.InitializeAsync();
        Assert.True(fixture.Client.IsUnavailable);
        Assert.Null(fixture.AuthenticationState.AccessToken);
        Assert.False((await fixture.AuthenticationState.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        await fixture.Client.InitializeAsync();
        Assert.False(fixture.Client.IsUnavailable);
        Assert.Equal("restored-token", fixture.AuthenticationState.AccessToken);
        Assert.Equal("device-session", fixture.AuthenticationState.SessionId);
    }

    [Fact]
    public async Task BackgroundRefreshDoesNotSubmitUserActivity()
    {
        string? body = null;
        using var fixture = CreateFixture(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        Assert.Equal(CurrentUserStatus.Unauthorized, await fixture.Client.RefreshAsync());
        Assert.Contains("\"userActivity\":false", body);
        Assert.False(fixture.Client.IsUnavailable);
        Assert.Null(fixture.AuthenticationState.AccessToken);
    }

    [Fact]
    public async Task DelayedRefreshCannotRestoreOldAccountAfterCrossTabTransition()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = CreateFixture(_ => pending.Task);
        var refresh = fixture.Client.RefreshAsync();
        await fixture.Client.SessionChanged();
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginResponse(
            "old-account-token", DateTimeOffset.UtcNow.AddMinutes(15), new("old@example.test", "Tidligere konto", ["User"]), "old-session")) });
        Assert.Equal(CurrentUserStatus.Unavailable, await refresh);
        Assert.Null(fixture.AuthenticationState.AccessToken);
        Assert.False((await fixture.AuthenticationState.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task IntentionalLoginAfterPendingStartupRejectionAppliesSuccessfulSession()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loginCalls = 0;
        using var fixture = CreateFixture(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal)) return pending.Task;
            loginCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginResponse(
                "login-token", DateTimeOffset.UtcNow.AddMinutes(15), new("new@example.test", "Ny konto", ["User"]), "new-session")) });
        });
        fixture.AuthenticationState.ClearSession();
        var startup = fixture.Client.InitializeAsync();
        var login = fixture.Client.LoginAsync(new LoginRequest { Email = "new@example.test", Password = "synthetic" });
        Assert.False(login.IsCompleted);
        Assert.Equal(0, loginCalls);
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await startup;
        Assert.Null(await login);
        Assert.Equal(1, loginCalls);
        Assert.Equal("login-token", fixture.AuthenticationState.AccessToken);
        Assert.Equal("new-session", fixture.AuthenticationState.SessionId);
        Assert.True((await fixture.AuthenticationState.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False(fixture.Client.IsUnavailable);
    }

    [Fact]
    public async Task LogoutSupersedesLoginWaitingForStartupRestoration()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loginCalls = 0;
        using var fixture = CreateFixture(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal)) return pending.Task;
            if (request.RequestUri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) loginCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var startup = fixture.Client.InitializeAsync();
        var login = fixture.Client.LoginAsync(new LoginRequest { Email = "new@example.test", Password = "synthetic" });
        Assert.Equal(LogoutStatus.Succeeded, await fixture.Client.LogoutAsync());
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await startup;
        Assert.NotNull(await login);
        Assert.Equal(0, loginCalls);
        Assert.Null(fixture.AuthenticationState.AccessToken);
    }

    [Fact]
    public async Task ExpiredAccessJwtRefreshesInMemoryBeforeProtectedRequest()
    {
        var state = CreateAuthenticatedState();
        state.SetSession(new("expired-token", DateTimeOffset.UtcNow.AddMinutes(-1), new("a@example.test", "A", ["User"]), "device"));
        var navigation = new TestNavigationManager();
        using var transport = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("renewed-token", DateTimeOffset.UtcNow.AddMinutes(15), new("a@example.test", "A", ["User"]), "device"))
        }))) { BaseAddress = new Uri(navigation.BaseUri) };
        var auth = new AuthenticationClient(new TestHttpClientFactory(transport), state, navigation);
        string? tokenUsed = null;
        using var handler = new ApiAuthorizationHandler(state, navigation, auth)
        {
            InnerHandler = new StubHttpMessageHandler(request =>
            {
                tokenUsed = request.Headers.Authorization?.Parameter;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/protected")).StatusCode);
        Assert.Equal("renewed-token", tokenUsed);
        Assert.Equal("renewed-token", state.AccessToken);
    }

    [Fact]
    public async Task UnconfirmedLogoutSuppressesRestorationAcrossClientRecreation()
    {
        var runtime = new SessionJavaScript();
        using var fixture = CreateFixture(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var auth = new AuthenticationClient(new TestHttpClientFactory(fixture.HttpClient), fixture.AuthenticationState, fixture.Navigation, runtime);
        Assert.Equal(LogoutStatus.ServerLogoutUnconfirmed, await auth.LogoutAsync());
        Assert.True(runtime.Suppressed);
        var reopenedState = new InMemoryAuthenticationStateProvider();
        var reopened = new AuthenticationClient(new TestHttpClientFactory(fixture.HttpClient), reopenedState, fixture.Navigation, runtime);
        await reopened.InitializeAsync();
        Assert.True(reopened.LogoutPending);
        Assert.Equal(0, runtime.RefreshRequests);
        Assert.Null(reopenedState.AccessToken);
    }

    [Fact]
    public async Task TemporaryRefreshFailureDoesNotSendProtectedRequestOrRedirectToLogin()
    {
        var state = CreateAuthenticatedState();
        state.SetSession(new("expired-token", DateTimeOffset.UtcNow.AddMinutes(-1), new("a@example.test", "A", ["User"]), "device"));
        var navigation = new TestNavigationManager();
        using var transport = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
            { BaseAddress = new Uri(navigation.BaseUri) };
        var auth = new AuthenticationClient(new TestHttpClientFactory(transport), state, navigation);
        var protectedCalls = 0;
        using var handler = new ApiAuthorizationHandler(state, navigation, auth)
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
            {
                protectedCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("api/protected")).StatusCode);
        Assert.Equal(0, protectedCalls);
        Assert.True(auth.IsUnavailable);
        Assert.Null(state.AccessToken);
        Assert.Equal("https://fitness.test/", navigation.Uri);
    }

    [Theory]
    [InlineData("/login", false)]
    [InlineData("/registrer", false)]
    [InlineData("/glemt-adgangskode", false)]
    [InlineData("/nulstil-adgangskode?email=test%40example.test&token=synthetic", false)]
    [InlineData("/", true)]
    [InlineData("/styrke", true)]
    [InlineData("/login/protected", true)]
    public void SessionGateAllowsPublicRecoveryAndBlocksProtectedRoutes(string path, bool expectedGate)
    {
        Assert.Equal(expectedGate, SessionRouting.ShowSessionGate(true, "https://fitness.test" + path));
        Assert.False(SessionRouting.ShowSessionGate(false, "https://fitness.test" + path));
    }

    [Fact]
    public async Task TemporaryUnavailableRetainsExpectedDeviceIdentityAcrossRetries()
    {
        var state = CreateAuthenticatedState();
        state.SetSession(new("old-token", DateTimeOffset.UtcNow.AddMinutes(15), new("a@example.test", "A", ["User"]), "original-device"));
        var navigation = new TestNavigationManager();
        var submitted = new List<RefreshRequest>();
        using var transport = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            submitted.Add((await request.Content!.ReadFromJsonAsync<RefreshRequest>())!);
            return new HttpResponseMessage(submitted.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict);
        })) { BaseAddress = new Uri(navigation.BaseUri) };
        var auth = new AuthenticationClient(new TestHttpClientFactory(transport), state, navigation);
        Assert.Equal(CurrentUserStatus.Unavailable, await auth.RefreshAsync());
        Assert.Null(state.AccessToken);
        Assert.Equal("original-device", state.SessionId);
        await auth.InitializeAsync();
        Assert.All(submitted, request => Assert.Equal("original-device", request.ExpectedSessionId));
        Assert.Null(state.AccessToken);
        Assert.True(auth.IsUnavailable);
    }

    [Fact]
    public async Task AccountTransitionDuringRetryBodyReadPreventsProtectedDispatch()
    {
        var state = CreateAuthenticatedState();
        var navigation = new TestNavigationManager();
        using var transport = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("refreshed-token", DateTimeOffset.UtcNow.AddMinutes(15), new("a@example.test", "A", ["User"]), "device"))
        }))) { BaseAddress = new Uri(navigation.BaseUri) };
        var auth = new AuthenticationClient(new TestHttpClientFactory(transport), state, navigation);
        var protectedCalls = 0;
        using var handler = new ApiAuthorizationHandler(state, navigation, auth)
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
            {
                protectedCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/protected")
        {
            Content = new TransitionContent(state)
        };
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(1, protectedCalls);
        Assert.Null(state.AccessToken);
    }

    [Fact]
    public async Task DefinitiveSessionRejectionInvalidatesDelayedCurrentUserResponse()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = CreateFixture(_ => pending.Task);
        var load = fixture.Client.GetCurrentUserAsync();
        fixture.AuthenticationState.ClearSession();
        pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AuthenticatedUserResponse("old@example.test", "Tidligere", ["User"]))
        });
        Assert.Equal(CurrentUserStatus.Unavailable, (await load).Status);
        Assert.Null(fixture.AuthenticationState.AccessToken);
        Assert.False((await fixture.AuthenticationState.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task AccountTransitionAfterPreflightRefreshPreventsInitialProtectedDispatch()
    {
        var state = CreateAuthenticatedState();
        state.SetSession(new("expired-token", DateTimeOffset.UtcNow.AddMinutes(-1), new("a@example.test", "A", ["User"]), "device"));
        var navigation = new TestNavigationManager();
        using var transport = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("refreshed-token", DateTimeOffset.UtcNow.AddMinutes(15), new("a@example.test", "A", ["User"]), "device"))
        }))) { BaseAddress = new Uri(navigation.BaseUri) };
        var auth = new AuthenticationClient(new TestHttpClientFactory(transport), state, navigation);
        auth.StateChanged += state.BeginTransition;
        var protectedCalls = 0;
        using var handler = new ApiAuthorizationHandler(state, navigation, auth)
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
            {
                protectedCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(navigation.BaseUri) };
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("api/protected")).StatusCode);
        Assert.Equal(0, protectedCalls);
    }

    private sealed class TransitionContent(InMemoryAuthenticationStateProvider state) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            state.BeginTransition();
            await stream.WriteAsync(new byte[] { 1 });
        }
        protected override bool TryComputeLength(out long length) { length = 1; return true; }
    }

    private sealed class SessionJavaScript : IJSRuntime
    {
        public bool Suppressed { get; private set; }
        public int RefreshRequests { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = null;
            if (identifier == "fitnessSession.suppress") Suppressed = true;
            if (identifier == "fitnessSession.pendingLogout") result = Suppressed;
            if (identifier == "fitnessSession.foregroundOpening") result = false;
            if (identifier == "fitnessSession.request")
            {
                if ((string)args![0]! == "refresh") RefreshRequests++;
                result = new { Status = 503, Body = "" };
            }
            return new ValueTask<TValue>(result is null ? default! : JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(result))!);
        }
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
