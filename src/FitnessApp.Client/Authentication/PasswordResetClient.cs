using System.Net;
using System.Net.Http.Json;
using FitnessApp.Contracts.Authentication;

namespace FitnessApp.Client.Authentication;

public sealed class PasswordResetClient(IHttpClientFactory httpClientFactory)
{
    public async Task<ForgotPasswordSubmissionResult> RequestAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "api/auth/password-reset/request",
            request,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Accepted)
        {
            var confirmation = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>(cancellationToken);
            return ForgotPasswordSubmissionResult.Succeeded(
                confirmation?.Message ??
                "Hvis der findes en godkendt konto med denne e-mailadresse, modtager du en mail med et link til at nulstille din adgangskode.");
        }

        return response.StatusCode is HttpStatusCode.TooManyRequests
            ? ForgotPasswordSubmissionResult.Failed("For mange forsøg. Prøv igen senere.")
            : ForgotPasswordSubmissionResult.Failed("Anmodningen kunne ikke sendes. Prøv igen.");
    }

    public async Task<PasswordResetSubmissionResult> ResetAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "api/auth/password-reset/complete",
            request,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent)
        {
            return PasswordResetSubmissionResult.Succeeded();
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            return PasswordResetSubmissionResult.Failed(
                "For mange forsøg. Prøv igen senere.");
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(cancellationToken);
            var passwordError = problem?.Errors?
                .Where(error => error.Key.EndsWith(nameof(ResetPasswordRequest.NewPassword), StringComparison.Ordinal))
                .SelectMany(error => error.Value)
                .FirstOrDefault();
            return passwordError is not null
                ? PasswordResetSubmissionResult.PasswordRejected(passwordError)
                : PasswordResetSubmissionResult.InvalidLink();
        }

        return PasswordResetSubmissionResult.Failed(
            "Adgangskoden kunne ikke nulstilles. Prøv igen.");
    }

    private HttpClient Client => httpClientFactory.CreateClient(AuthenticationClient.ClientName);

    private sealed record ValidationProblemResponse(
        string? Title,
        Dictionary<string, string[]>? Errors);
}

public sealed record ForgotPasswordSubmissionResult(
    bool IsSuccess,
    string Message)
{
    public static ForgotPasswordSubmissionResult Succeeded(string message) => new(true, message);

    public static ForgotPasswordSubmissionResult Failed(string message) => new(false, message);
}

public enum PasswordResetSubmissionStatus
{
    Succeeded,
    InvalidLink,
    PasswordRejected,
    Failed
}

public sealed record PasswordResetSubmissionResult(
    PasswordResetSubmissionStatus Status,
    string? ErrorMessage = null)
{
    public static PasswordResetSubmissionResult Succeeded() =>
        new(PasswordResetSubmissionStatus.Succeeded);

    public static PasswordResetSubmissionResult InvalidLink() =>
        new(PasswordResetSubmissionStatus.InvalidLink);

    public static PasswordResetSubmissionResult PasswordRejected(string message) =>
        new(PasswordResetSubmissionStatus.PasswordRejected, message);

    public static PasswordResetSubmissionResult Failed(string message) =>
        new(PasswordResetSubmissionStatus.Failed, message);
}
