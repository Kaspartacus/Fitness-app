using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Running;

namespace FitnessApp.Client.Running;

public sealed record RunningClientResult<T>(
    T? Value,
    string? Error = null,
    bool IsConflict = false,
    bool IsNotFound = false,
    bool IsValidationError = false,
    bool IsCanceled = false,
    bool IsUnavailable = false,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors = null,
    Guid? CurrentResultId = null,
    Guid? CurrentSessionId = null)
{
    public bool Succeeded => Error is null && !IsCanceled;
}

public sealed class RunningClient(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<RunningClientResult<RunningOverviewResponse>> GetOverviewAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningOverviewResponse>(HttpMethod.Get, "api/running/overview", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningPlanResponse>> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningPlanResponse>(HttpMethod.Get, $"api/running/plans/{planId}", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningPlanResponse>> CreatePlanAsync(
        RunningPlanRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningPlanResponse>(HttpMethod.Post, "api/running/plans", request, cancellationToken);

    public Task<RunningClientResult<RunningPlanResponse>> ReplacePlanAsync(
        Guid activePlanId,
        ReplaceRunningPlanRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningPlanResponse>(HttpMethod.Put, $"api/running/plans/{activePlanId}", request, cancellationToken);

    public Task<RunningClientResult<List<RunningSessionResponse>>> ListSessionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return Task.FromResult(Validation<List<RunningSessionResponse>>(
                "Slutdatoen skal ligge på eller efter startdatoen."));
        }

        if (to.DayNumber - from.DayNumber + 1 > 93)
        {
            return Task.FromResult(Validation<List<RunningSessionResponse>>(
                "Kalenderperioden må højst omfatte 93 dage."));
        }

        var start = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return SendAsync<List<RunningSessionResponse>>(
            HttpMethod.Get,
            $"api/running/sessions?from={start}&to={end}",
            cancellationToken: cancellationToken);
    }

    public Task<RunningClientResult<RunningSessionResponse>> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningSessionResponse>(HttpMethod.Get, $"api/running/sessions/{sessionId}", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningSessionResponse>> StartSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningSessionResponse>(HttpMethod.Post, $"api/running/sessions/{sessionId}/start", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningSessionResponse>> CancelSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningSessionResponse>(HttpMethod.Post, $"api/running/sessions/{sessionId}/cancel", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningResultResponse>> CreateManualResultAsync(
        ManualRunningResultRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningResultResponse>(HttpMethod.Post, "api/running/results/manual", request, cancellationToken);

    public Task<RunningClientResult<RunningResultResponse>> CompleteSessionAsync(
        Guid sessionId,
        CompleteRunningSessionRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningResultResponse>(HttpMethod.Post, $"api/running/sessions/{sessionId}/complete", request, cancellationToken);

    public Task<RunningClientResult<RunningResultResponse>> GetResultAsync(
        Guid resultId,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningResultResponse>(HttpMethod.Get, $"api/running/results/{resultId}", cancellationToken: cancellationToken);

    public Task<RunningClientResult<RunningResultResponse>> UpdateResultAsync(
        Guid resultId,
        UpdateRunningResultRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RunningResultResponse>(HttpMethod.Put, $"api/running/results/{resultId}", request, cancellationToken);

    private async Task<RunningClientResult<T>> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await Client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.NoContent ||
                    response.Content.Headers.ContentLength is 0)
                {
                    return new(default);
                }

                try
                {
                    var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                    return value is null
                        ? Unavailable<T>("Serveren returnerede ikke et gyldigt svar. Prøv igen.")
                        : new(value);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    return Unavailable<T>("Serveren returnerede ikke et gyldigt svar. Prøv igen.");
                }
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            return Failure<T>(response.StatusCode, problem);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(default, "Anmodningen blev annulleret.", IsCanceled: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return Unavailable<T>("Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }

    private HttpClient Client => httpClientFactory.CreateClient(AuthenticationClient.ClientName);

    private static RunningClientResult<T> Failure<T>(HttpStatusCode statusCode, ProblemMessage problem) => statusCode switch
    {
        HttpStatusCode.NotFound => new(default,
            problem.Message ?? "Det ønskede løbedata blev ikke fundet.",
            IsNotFound: true),
        HttpStatusCode.Conflict => new(default,
            problem.Message ?? "Data er ændret et andet sted. Genindlæs, og prøv igen.",
            IsConflict: true,
            CurrentResultId: problem.ResultId,
            CurrentSessionId: problem.SessionId),
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new(default,
            problem.Message ?? "Kontrollér dine oplysninger, og prøv igen.",
            IsValidationError: true,
            ValidationErrors: problem.ValidationErrors),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Unavailable<T>(
            problem.Message ?? "Du skal være logget ind med en godkendt konto."),
        HttpStatusCode.TooManyRequests => Unavailable<T>(
            problem.Message ?? "For mange forsøg. Prøv igen senere."),
        _ => Unavailable<T>(
            problem.Message ?? "Løbedata kunne ikke hentes eller gemmes. Prøv igen.")
    };

    private static RunningClientResult<T> Validation<T>(string message) => new(
        default,
        message,
        IsValidationError: true);

    private static RunningClientResult<T> Unavailable<T>(string message) => new(
        default,
        message,
        IsUnavailable: true);

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

            var errors = problem.Errors?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is { Length: > 0 })
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray());
            var fieldMessage = errors?.Values.SelectMany(messages => messages).FirstOrDefault();
            return new(fieldMessage ?? FirstText(problem.Title, problem.Detail), errors, problem.ResultId, problem.SessionId);
        }
        catch (JsonException)
        {
            return ProblemMessage.Empty;
        }
    }

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record ProblemResponse(
        string? Title,
        string? Detail,
        Dictionary<string, string[]>? Errors,
        Guid? ResultId,
        Guid? SessionId);

    private sealed record ProblemMessage(
        string? Message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors,
        Guid? ResultId,
        Guid? SessionId)
    {
        public static ProblemMessage Empty { get; } = new(null, null, null, null);
    }
}
