using System.Net.Http.Json;
using FitnessApp.Client.Authentication;
using FitnessApp.Contracts.Diary;
using Microsoft.AspNetCore.Components.Authorization;

namespace FitnessApp.Client.Diary;

public sealed record DiaryReply<T>(T? Value, int Status, string? Error = null)
{
    public bool Succeeded => Status is >= 200 and < 300;
}

// Only the unlock credential lives here, never diary text. Nothing is written to browser storage.
public sealed class DiaryClient : IDisposable
{
    private readonly IHttpClientFactory clients;
    private readonly InMemoryAuthenticationStateProvider authentication;
    private string? grant, session;
    private long generation, epoch;
    public event Action? Locked;
    public DiaryClient(IHttpClientFactory clients, InMemoryAuthenticationStateProvider authentication)
    {
        this.clients = clients; this.authentication = authentication;
        session = authentication.SessionId; generation = authentication.Generation;
        authentication.AuthenticationStateChanged += AuthenticationChanged;
    }
    private void AuthenticationChanged(Task<AuthenticationState> _)
    {
        if (session != authentication.SessionId || generation != authentication.Generation)
        {
            session = authentication.SessionId; generation = authentication.Generation;
            Clear();
        }
    }
    public void Clear() { grant = null; epoch++; Locked?.Invoke(); }
    public async Task LockAsync()
    {
        var request = SendAsync<bool>(HttpMethod.Post, "lock");
        Clear();
        await request;
    }
    public async Task<DiaryReply<DiaryUnlockResponse>> UnlockAsync(string code)
    {
        var result = await SendAsync<DiaryUnlockResponse>(HttpMethod.Post, "unlock", new DiaryCodeRequest(code));
        if (result.Succeeded) grant = result.Value?.Grant;
        return result;
    }
    public async Task<DiaryReply<DiaryUnlockResponse>> ConfigureAsync(string? currentCode, string? newCode, string? confirmation)
    {
        var result = await SendAsync<DiaryUnlockResponse>(HttpMethod.Put, "protection", new ConfigureDiaryRequest(currentCode, newCode, confirmation));
        if (result.Succeeded) Clear();
        return result;
    }
    public async Task<DiaryReply<bool>> RecoverAsync(string accountPassword)
    {
        var result = await SendAsync<bool>(HttpMethod.Post, "recover", new RecoverDiaryRequest(accountPassword));
        if (result.Succeeded) Clear();
        return result;
    }
    public Task<DiaryReply<DiaryStatusResponse>> StateAsync() => SendAsync<DiaryStatusResponse>(HttpMethod.Get, "state");
    public Task<DiaryReply<bool>> ActivityAsync() => SendAsync<bool>(HttpMethod.Post, "activity");
    public Task<DiaryReply<DiaryDateResponse[]>> DatesAsync(DateOnly month) => SendAsync<DiaryDateResponse[]>(HttpMethod.Get, $"dates/{month.Year}/{month.Month}");
    public Task<DiaryReply<DiaryEntryResponse>> ReadAsync(DateOnly date) => SendAsync<DiaryEntryResponse>(HttpMethod.Get, $"entries/{date:yyyy-MM-dd}");
    public Task<DiaryReply<DiaryEntryResponse>> SaveAsync(DateOnly date, string text, Guid? version) => SendAsync<DiaryEntryResponse>(HttpMethod.Put, $"entries/{date:yyyy-MM-dd}", new SaveDiaryRequest(text, version));
    public Task<DiaryReply<bool>> DeleteAsync(DateOnly date, Guid version) => SendAsync<bool>(HttpMethod.Delete, $"entries/{date:yyyy-MM-dd}/{version}");
    private async Task<DiaryReply<T>> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        var started = epoch; var authGeneration = authentication.Generation;
        try
        {
            using var request = new HttpRequestMessage(method, "api/diary/" + path);
            if (grant is not null) request.Headers.Add("X-Diary-Grant", grant);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await clients.CreateClient(AuthenticationClient.ClientName).SendAsync(request);
            if (started != epoch || authGeneration != authentication.Generation) return new(default, 423, "Dagbogen er låst.");
            if ((int)response.StatusCode is 401 or 403 or 423)
            {
                Clear();
                return new(default, (int)response.StatusCode, "Lås dagbogen op for at fortsætte.");
            }
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>();
                if (started != epoch || authGeneration != authentication.Generation) return new(default, 423, "Dagbogen er låst.");
                return new(value, (int)response.StatusCode);
            }
            // Constant UI messages keep server or transport exception text out of the diary flow.
            return new(default, (int)response.StatusCode, (int)response.StatusCode switch
            {
                409 => "En anden fane har ændret siden. Din kladde er bevaret. Kopiér den om nødvendigt, og genindlæs den gemte version.",
                429 => "For mange forsøg. Vent fem minutter, og prøv igen.",
                404 => "Der er endnu ingen gemt side på denne dato.",
                400 => "Kontrollér koden eller adgangskoden. Nye dagbogskoder skal være ens og indeholde 1–128 tegn.",
                503 => "Siden kan ikke åbnes lige nu. Prøv igen eller kontakt den driftsansvarlige.",
                _ => "Dagbogen kunne ikke hentes eller gemmes. Prøv igen."
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        { return new(default, 503, "Kunne ikke kontakte serveren. Din kladde er kun i denne fane."); }
    }
    public void Dispose() => authentication.AuthenticationStateChanged -= AuthenticationChanged;
}
