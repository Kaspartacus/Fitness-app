using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Strength;

namespace FitnessApp.Client.Strength;

public sealed record ProgramClientResult<T>(T? Value, string? Error = null, bool Conflict = false, bool NotFound = false)
{
    public bool Succeeded => Error is null;
}

public sealed class StrengthProgramClient(IHttpClientFactory factory)
{
    public Task<ProgramClientResult<List<ProgramResponse>>> ListAsync() =>
        Send<List<ProgramResponse>>(HttpMethod.Get, "api/strength/programs/");
    public Task<ProgramClientResult<ProgramResponse>> GetAsync(Guid id) =>
        Send<ProgramResponse>(HttpMethod.Get, $"api/strength/programs/{id}");
    public Task<ProgramClientResult<ProgramResponse>> SaveAsync(Guid? id, SaveProgramRequest request) =>
        Send<ProgramResponse>(id is null ? HttpMethod.Post : HttpMethod.Put,
            id is null ? "api/strength/programs/" : $"api/strength/programs/{id}", request);
    public Task<ProgramClientResult<object>> DeleteAsync(Guid id, Guid version) =>
        Send<object>(HttpMethod.Delete, $"api/strength/programs/{id}?version={version}");

    private async Task<ProgramClientResult<T>> Send<T>(HttpMethod method, string url, SaveProgramRequest? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await factory.CreateClient(AuthenticationClient.ClientName).SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(default);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>();
                if (value is not null) return new(value);
            }
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => new(default, "Programmet blev ikke fundet. Det kan være slettet.", NotFound: true),
                HttpStatusCode.Conflict => new(default, "Programmet er ændret i en anden fane. Dit udkast er bevaret. Genindlæs for at se den nyeste version.", Conflict: true),
                HttpStatusCode.BadRequest => new(default, "Kontrollér navnene, antallet af øvelser, sæt og gentagelser, og prøv igen."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(default, "Du skal være logget ind med en godkendt konto."),
                _ => new(default, "Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(default, "Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }
}
