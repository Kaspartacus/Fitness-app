using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitnessApp.Contracts.Nutrition;

namespace FitnessApp.Client.Nutrition;

public sealed class NutritionClient(IHttpClientFactory clients)
{
    private HttpClient Client => clients.CreateClient(FitnessApp.Client.Authentication.AuthenticationClient.ClientName);
    public async Task<NutritionClientResult<NutritionDayResponse>> GetDayAsync(DateOnly date, CancellationToken ct) => await SendAsync<NutritionDayResponse>(Client.GetAsync($"api/nutrition/days/{date:yyyy-MM-dd}", ct), ct);
    public async Task<NutritionClientResult<FoodSearchPageResponse>> SearchAsync(string query, int page, CancellationToken ct) => await SendAsync<FoodSearchPageResponse>(Client.GetAsync($"api/nutrition/foods?query={Uri.EscapeDataString(query)}&page={page}&pageSize=20", ct), ct);
    public async Task<NutritionClientResult<FoodSearchResponse>> CreateCustomFoodAsync(CreateCustomNutritionFoodRequest request, CancellationToken ct) => await SendAsync<FoodSearchResponse>(Client.PostAsJsonAsync("api/nutrition/foods/custom", request, ct), ct);
    public async Task<NutritionClientResult<NutritionDayResponse>> AddAsync(AddNutritionFoodRequest request, CancellationToken ct) => await SendAsync<NutritionDayResponse>(Client.PostAsJsonAsync("api/nutrition/entries", request, ct), ct);
    public async Task<NutritionClientResult<NutritionDayResponse>> UpdateAsync(Guid id, decimal grams, CancellationToken ct) => await SendAsync<NutritionDayResponse>(Client.PutAsJsonAsync($"api/nutrition/entries/{id}", new UpdateNutritionFoodRequest { Grams = grams }, ct), ct);
    public async Task<NutritionClientResult<NutritionDayResponse>> DeleteAsync(Guid id, CancellationToken ct) => await SendAsync<NutritionDayResponse>(Client.DeleteAsync($"api/nutrition/entries/{id}", ct), ct);
    public async Task<NutritionClientResult<IReadOnlyList<NutritionRecipeResponse>>> RecipesAsync(CancellationToken ct) => await SendAsync<IReadOnlyList<NutritionRecipeResponse>>(Client.GetAsync("api/nutrition/recipes", ct), ct);
    public async Task<NutritionClientResult<bool>> SaveRecipeAsync(CreateNutritionRecipeRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("api/nutrition/recipes", request, ct);
            return response.IsSuccessStatusCode
                ? new(true, null)
                : new(false, await ReadErrorAsync(response, ct));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "Forespørgslen tog for lang tid. Prøv igen.");
        }
        catch (HttpRequestException)
        {
            return new(false, "Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen.");
        }
    }
    public async Task<NutritionClientResult<NutritionDayResponse>> AddRecipeAsync(Guid recipeId, AddNutritionRecipeRequest request, CancellationToken ct) => await SendAsync<NutritionDayResponse>(Client.PostAsJsonAsync($"api/nutrition/recipes/{recipeId}/entries", request, ct), ct);

    private static async Task<NutritionClientResult<T>> SendAsync<T>(Task<HttpResponseMessage> operation, CancellationToken ct)
    {
        try
        {
            using var response = await operation;
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent) return new(default, null);
                return new(await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct), null);
            }
            return new(default, await ReadErrorAsync(response, ct));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(default, "Forespørgslen tog for lang tid. Prøv igen."); }
        catch (HttpRequestException) { return new(default, "Kunne ikke kontakte serveren. Kontrollér forbindelsen, og prøv igen."); }
        catch (JsonException) { return new(default, "Serveren returnerede et ugyldigt svar. Prøv igen."); }
    }
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { var problem = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct); return problem?.Title ?? "Handlingen kunne ikke gennemføres. Prøv igen."; }
        catch { return "Handlingen kunne ikke gennemføres. Prøv igen."; }
    }
}

public sealed record ApiError(string? Title);

public sealed record NutritionClientResult<T>(T? Value, string? Error) { public bool IsSuccess => Error is null; }
