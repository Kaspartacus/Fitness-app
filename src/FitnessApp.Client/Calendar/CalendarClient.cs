using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Calendar;

namespace FitnessApp.Client.Calendar;

public sealed record CalendarClientResult<T>(T? Value, string? Error = null, bool IsCanceled = false)
{
    public bool Succeeded => Error is null && !IsCanceled;
}

public sealed class CalendarClient(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CalendarClientResult<CalendarRangeResponse>> GetRangeAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > 93)
        {
            return new(default, "Kalenderperioden er ugyldig.");
        }

        var start = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            using var response = await httpClientFactory.CreateClient(AuthenticationClient.ClientName)
                .GetAsync($"api/calendar?from={start}&to={end}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var range = await response.Content.ReadFromJsonAsync<CalendarRangeResponse>(JsonOptions, cancellationToken);
                return range is null
                    ? new(default, "Serveren returnerede ikke et gyldigt kalendersvar. Prøv igen.")
                    : new(range);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(default,
                    "Du skal være logget ind med en godkendt konto."),
                _ => new(default, "Kalenderen kunne ikke indlæses. Prøv igen.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(default, "Anmodningen blev annulleret.", true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return new(default, "Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }
}
