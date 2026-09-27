using System.Net;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.Client.Authentication;

public sealed class UserAdministrationClient(IHttpClientFactory httpClientFactory)
{
    public async Task<PendingRegistrationCountResult> GetPendingCountAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync("api/admin/registrations/pending-count", cancellationToken);
            if (response.StatusCode is HttpStatusCode.Forbidden)
            {
                return PendingRegistrationCountResult.Forbidden();
            }

            if (!response.IsSuccessStatusCode)
            {
                return PendingRegistrationCountResult.Unavailable();
            }

            var result = await response.Content.ReadFromJsonAsync<PendingRegistrationCountResponse>(cancellationToken);
            return result is null
                ? PendingRegistrationCountResult.Unavailable()
                : PendingRegistrationCountResult.Succeeded(result.PendingCount);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return PendingRegistrationCountResult.Unavailable();
        }
    }

    public async Task<PendingRegistrationLoadResult> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync("api/admin/registrations/pending?limit=50", cancellationToken);
            if (response.StatusCode is HttpStatusCode.Forbidden)
            {
                return PendingRegistrationLoadResult.Forbidden();
            }

            if (!response.IsSuccessStatusCode)
            {
                return PendingRegistrationLoadResult.Unavailable();
            }

            var result = await response.Content.ReadFromJsonAsync<PendingRegistrationListResponse>(cancellationToken);
            return result is null
                ? PendingRegistrationLoadResult.Unavailable()
                : PendingRegistrationLoadResult.Succeeded(result.Registrations);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return PendingRegistrationLoadResult.Unavailable();
        }
    }

    public async Task<RegistrationDecisionClientResult> DecideAsync(
        string registrationId,
        bool approve,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var action = approve ? "approve" : "reject";
            using var response = await Client.PostAsync(
                $"api/admin/registrations/{Uri.EscapeDataString(registrationId)}/{action}",
                content: null,
                cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => RegistrationDecisionClientResult.Completed,
                HttpStatusCode.Conflict or HttpStatusCode.NotFound =>
                    RegistrationDecisionClientResult.AlreadyProcessed,
                HttpStatusCode.Forbidden => RegistrationDecisionClientResult.Forbidden,
                _ => RegistrationDecisionClientResult.Unavailable
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return RegistrationDecisionClientResult.Unavailable;
        }
    }

    private HttpClient Client => httpClientFactory.CreateClient(AuthenticationClient.ClientName);
}

public enum PendingRegistrationLoadStatus
{
    Succeeded,
    Forbidden,
    Unavailable
}

public enum PendingRegistrationCountStatus
{
    Succeeded,
    Forbidden,
    Unavailable
}

public sealed record PendingRegistrationCountResult(
    PendingRegistrationCountStatus Status,
    int PendingCount)
{
    public static PendingRegistrationCountResult Succeeded(int pendingCount) =>
        new(PendingRegistrationCountStatus.Succeeded, pendingCount);

    public static PendingRegistrationCountResult Forbidden() =>
        new(PendingRegistrationCountStatus.Forbidden, 0);

    public static PendingRegistrationCountResult Unavailable() =>
        new(PendingRegistrationCountStatus.Unavailable, 0);
}

public sealed record PendingRegistrationLoadResult(
    PendingRegistrationLoadStatus Status,
    IReadOnlyCollection<PendingRegistrationResponse> Registrations)
{
    public static PendingRegistrationLoadResult Succeeded(
        IReadOnlyCollection<PendingRegistrationResponse> registrations) =>
        new(PendingRegistrationLoadStatus.Succeeded, registrations);

    public static PendingRegistrationLoadResult Forbidden() =>
        new(PendingRegistrationLoadStatus.Forbidden, []);

    public static PendingRegistrationLoadResult Unavailable() =>
        new(PendingRegistrationLoadStatus.Unavailable, []);
}

public enum RegistrationDecisionClientResult
{
    Completed,
    AlreadyProcessed,
    Forbidden,
    Unavailable
}
