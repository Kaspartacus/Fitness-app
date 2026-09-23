using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Settings;

namespace FitnessApp.Client.Settings;

public sealed record SettingsClientResult<T>(
    T? Value,
    string? Error = null,
    bool IsValidationError = false,
    bool IsNotFound = false,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors = null)
{
    public bool Succeeded => Error is null;
}

public sealed record SettingsClientActionResult(string? Error = null, bool IsNotFound = false)
{
    public bool Succeeded => Error is null;
}

public sealed class SettingsClient(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SettingsClientResult<SettingsOverviewResponse>> GetOverviewAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<SettingsOverviewResponse>(HttpMethod.Get, "api/settings", cancellationToken: cancellationToken);

    public Task<SettingsClientResult<ProfileSettingsResponse>> UpdateProfileAsync(
        UpdateProfileSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProfileSettingsResponse>(HttpMethod.Put, "api/settings/profile", request, cancellationToken);

    public Task<SettingsClientResult<NotificationPreferencesResponse>> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<NotificationPreferencesResponse>(HttpMethod.Put, "api/settings/notification-preferences", request,
            cancellationToken);

    public Task<SettingsClientResult<GarminMockConnectionResponse>> ConnectGarminDemoAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<GarminMockConnectionResponse>(HttpMethod.Post, "api/settings/garmin-demo/connect",
            cancellationToken: cancellationToken);

    public Task<SettingsClientResult<GarminMockConnectionResponse>> DisconnectGarminDemoAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<GarminMockConnectionResponse>(HttpMethod.Post, "api/settings/garmin-demo/disconnect",
            cancellationToken: cancellationToken);

    public Task<SettingsClientResult<InAppNotificationListResponse>> GetNotificationsAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<InAppNotificationListResponse>(HttpMethod.Get, "api/settings/notifications",
            cancellationToken: cancellationToken);

    public async Task<SettingsClientActionResult> MarkNotificationReadAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.PostAsync(
                $"api/settings/notifications/{notificationId}/read",
                content: null,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new();
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => new(problem.Message ?? "Notifikationen findes ikke længere.", true),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(
                    problem.Message ?? "Du har ikke længere adgang til denne notifikation."),
                _ => new(problem.Message ?? "Notifikationen kunne ikke markeres som læst. Prøv igen.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new("Anmodningen blev annulleret.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return new("Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }

    private async Task<SettingsClientResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await Client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value is null
                    ? Unavailable<T>("Serveren returnerede ikke et gyldigt svar. Prøv igen.")
                    : new(value);
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new(
                    default,
                    problem.Message ?? "Kontrollér dine oplysninger, og prøv igen.",
                    true,
                    ValidationErrors: problem.ValidationErrors),
                HttpStatusCode.NotFound => new(
                    default,
                    problem.Message ?? "Indstillingen findes ikke længere.",
                    IsNotFound: true),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Unavailable<T>(
                    problem.Message ?? "Du skal være logget ind med en godkendt konto."),
                _ => Unavailable<T>(problem.Message ?? "Indstillingerne kunne ikke hentes eller gemmes. Prøv igen.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Unavailable<T>("Anmodningen blev annulleret.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return Unavailable<T>("Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }

    private HttpClient Client => httpClientFactory.CreateClient(AuthenticationClient.ClientName);

    private static SettingsClientResult<T> Unavailable<T>(string error) => new(default, error);

    private static async Task<ProblemMessage> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return ProblemMessage.Empty;
            }

            var problem = JsonSerializer.Deserialize<ProblemResponse>(content, JsonOptions);
            if (problem is null)
            {
                return ProblemMessage.Empty;
            }

            var validationErrors = problem.Errors?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is { Length: > 0 })
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .ToArray());
            var fieldMessage = validationErrors?.Values.SelectMany(messages => messages).FirstOrDefault();
            return new(fieldMessage ?? FirstText(problem.Title, problem.Detail), validationErrors);
        }
        catch (JsonException)
        {
            return ProblemMessage.Empty;
        }
    }

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record ProblemResponse(
        string? Title,
        string? Detail,
        Dictionary<string, string[]>? Errors);

    private sealed record ProblemMessage(
        string? Message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors)
    {
        public static ProblemMessage Empty { get; } = new(null, null);
    }
}
