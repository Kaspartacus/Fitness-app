using System.Net;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.Client.Authentication;

public sealed class RegistrationClient(IHttpClientFactory httpClientFactory)
{
    public async Task<RegistrationSubmissionResult> RegisterAsync(
        RegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsJsonAsync("api/registrations", request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Accepted)
        {
            return RegistrationSubmissionResult.Succeeded();
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            return RegistrationSubmissionResult.Failed("For mange forsøg. Prøv igen senere.");
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(cancellationToken);
            var message = problem?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault();
            return RegistrationSubmissionResult.Failed(
                message ?? "Kontrollér oplysningerne og prøv igen.");
        }

        return RegistrationSubmissionResult.Failed(
            "Registreringen kunne ikke gennemføres. Prøv igen.");
    }

    private HttpClient Client => httpClientFactory.CreateClient(AuthenticationClient.ClientName);

    private sealed record ValidationProblemResponse(Dictionary<string, string[]>? Errors);
}

public sealed record RegistrationSubmissionResult(bool IsSuccess, string? ErrorMessage)
{
    public static RegistrationSubmissionResult Succeeded() => new(true, null);

    public static RegistrationSubmissionResult Failed(string message) => new(false, message);
}
