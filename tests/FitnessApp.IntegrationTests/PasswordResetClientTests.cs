using System.Net;
using System.Net.Http.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.IntegrationTests;

public sealed class PasswordResetClientTests
{
    [Fact]
    public async Task ForgotPasswordClientHandlesAcceptedAndRateLimitedResponses()
    {
        using var acceptedFixture = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new ForgotPasswordResponse("Neutral besked"))
        });
        using var limitedFixture = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var accepted = await acceptedFixture.Client.RequestAsync(new ForgotPasswordRequest
        {
            Email = "user@example.test"
        });
        var limited = await limitedFixture.Client.RequestAsync(new ForgotPasswordRequest
        {
            Email = "user@example.test"
        });

        Assert.True(accepted.IsSuccess);
        Assert.Equal("Neutral besked", accepted.Message);
        Assert.False(limited.IsSuccess);
        Assert.Contains("For mange", limited.Message);
    }

    [Fact]
    public async Task ResetClientDistinguishesPasswordFailureInvalidLinkAndSuccess()
    {
        using var passwordFixture = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                errors = new Dictionary<string, string[]>
                {
                    [nameof(ResetPasswordRequest.NewPassword)] = ["Adgangskoden er for kort."]
                }
            })
        });
        using var invalidFixture = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { title = "Ugyldigt link" })
        });
        using var successFixture = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var request = new ResetPasswordRequest();

        var password = await passwordFixture.Client.ResetAsync(request);
        var invalid = await invalidFixture.Client.ResetAsync(request);
        var success = await successFixture.Client.ResetAsync(request);

        Assert.Equal(PasswordResetSubmissionStatus.PasswordRejected, password.Status);
        Assert.Equal("Adgangskoden er for kort.", password.ErrorMessage);
        Assert.Equal(PasswordResetSubmissionStatus.InvalidLink, invalid.Status);
        Assert.Equal(PasswordResetSubmissionStatus.Succeeded, success.Status);
    }

    private static ClientFixture CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var httpClient = new HttpClient(new StubHandler(responseFactory))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        return new ClientFixture(new PasswordResetClient(new StubHttpClientFactory(httpClient)), httpClient);
    }

    private sealed record ClientFixture(PasswordResetClient Client, HttpClient HttpClient) : IDisposable
    {
        public void Dispose() => HttpClient.Dispose();
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
