using System.Net;
using System.Text;
using FitnessApp.Client.Nutrition;
using FitnessApp.Contracts.Nutrition;

namespace FitnessApp.IntegrationTests;

public sealed class NutritionClientTests
{
    [Fact]
    public async Task SavingRecipeAcceptsEmptyCreatedResponse()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created);
        using var httpClient = new HttpClient(new FixedResponseHandler(response)) { BaseAddress = new Uri("https://fitness.example/") };
        var client = new NutritionClient(new FixedHttpClientFactory(httpClient));

        var result = await client.SaveRecipeAsync(new CreateNutritionRecipeRequest
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 26),
            MealSlot = NutritionMealSlot.Dinner,
            Name = "Aftensmad",
            Portions = 1
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task AddFoodReturnsErrorWhenServerSuccessBodyIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        using var httpClient = new HttpClient(new FixedResponseHandler(response)) { BaseAddress = new Uri("https://fitness.example/") };
        var client = new NutritionClient(new FixedHttpClientFactory(httpClient));

        var result = await client.AddAsync(new AddNutritionFoodRequest
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 26),
            MealSlot = NutritionMealSlot.Dinner,
            FoodId = 1,
            Grams = 100
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Serveren returnerede et ugyldigt svar. Prøv igen.", result.Error);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
