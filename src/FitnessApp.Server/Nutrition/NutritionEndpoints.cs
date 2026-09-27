using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Nutrition;
using FitnessApp.Contracts.Nutrition;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Server.Nutrition;

internal static class NutritionEndpoints
{
    public static void MapNutritionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/nutrition").RequireAuthorization();
        group.MapGet("/days/{date}", GetDayAsync);
        group.MapGet("/foods", SearchFoodsAsync);
        group.MapPost("/entries", AddFoodAsync);
        group.MapPut("/entries/{id:guid}", UpdateFoodAsync);
        group.MapDelete("/entries/{id:guid}", DeleteFoodAsync);
        group.MapGet("/recipes", ListRecipesAsync);
        group.MapPost("/recipes", CreateRecipeAsync);
        group.MapPost("/recipes/{id:guid}/entries", AddRecipeAsync);
    }

    private static async Task<IResult> GetDayAsync(string date, ClaimsPrincipal user, INutritionService service, CancellationToken ct) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day) ? Results.Ok(await service.GetDayAsync(Owner(user), day, ct)) : Invalid("Dato", "Angiv datoen på formen ÅÅÅÅ-MM-DD.");
    private static async Task<IResult> SearchFoodsAsync(string? query, int page, int pageSize, INutritionService service, CancellationToken ct) => Results.Ok(await service.SearchFoodsAsync(query ?? string.Empty, page, pageSize, ct));
    private static async Task<IResult> AddFoodAsync(AddNutritionFoodRequest? request, ClaimsPrincipal user, INutritionService service, CancellationToken ct) => request is null ? Invalid("Mad", "Vælg en fødevare og mængde.") : Status(await service.AddFoodAsync(Owner(user), new AddNutritionFoodInput(request.Id, request.Date, (FitnessApp.Domain.Nutrition.NutritionMealSlot)(int)request.MealSlot, request.FoodId, request.Grams), ct), true);
    private static async Task<IResult> UpdateFoodAsync(Guid id, UpdateNutritionFoodRequest? request, ClaimsPrincipal user, INutritionService service, CancellationToken ct) => request is null ? Invalid("Mængde", "Angiv en gyldig mængde i gram.") : Status(await service.UpdateFoodAsync(Owner(user), id, new UpdateNutritionFoodInput(request.Grams), ct));
    private static async Task<IResult> DeleteFoodAsync(Guid id, ClaimsPrincipal user, INutritionService service, CancellationToken ct) => Status(await service.DeleteFoodAsync(Owner(user), id, ct));
    private static async Task<IResult> ListRecipesAsync(ClaimsPrincipal user, INutritionService service, CancellationToken ct) => Results.Ok(await service.ListRecipesAsync(Owner(user), ct));
    private static async Task<IResult> CreateRecipeAsync(CreateNutritionRecipeRequest? request, ClaimsPrincipal user, INutritionService service, CancellationToken ct) => request is null ? Invalid("Måltid", "Angiv et navn og antal portioner.") : SimpleStatus(await service.CreateRecipeAsync(Owner(user), new CreateNutritionRecipeInput(request.Id, request.Date, (FitnessApp.Domain.Nutrition.NutritionMealSlot)(int)request.MealSlot, request.Name, request.Portions), ct), true);
    private static async Task<IResult> AddRecipeAsync(Guid id, AddNutritionRecipeRequest? request, ClaimsPrincipal user, INutritionService service, CancellationToken ct) => request is null ? Invalid("Måltid", "Angiv dato, måltid og antal portioner.") : Status(await service.AddRecipeAsync(Owner(user), id, new AddNutritionRecipeInput(request.Id, request.Date, (FitnessApp.Domain.Nutrition.NutritionMealSlot)(int)request.MealSlot, request.Portions), ct), true);
    private static IResult Status(NutritionOperationResult result, bool created = false) => result.Status switch
    {
        NutritionOperationStatus.Saved when result.Day is { } day => created ? Results.Created($"/api/nutrition/days/{day.Date:yyyy-MM-dd}", day) : Results.Ok(day),
        NutritionOperationStatus.NotFound => Results.NotFound(new { title = "Fødevare, måltid eller opskrift blev ikke fundet." }),
        NutritionOperationStatus.Conflict => Results.Conflict(new { title = "Kataloget er ikke importeret endnu, eller måltidet blev ændret i en anden fane." }),
        _ => Invalid("Ernæring", "Kontrollér fødevare, dato, måltid og mængde i gram.")
    };
    private static IResult SimpleStatus(NutritionOperationStatus status, bool created = false) => status switch
    {
        NutritionOperationStatus.Saved => created ? Results.Created("/api/nutrition/recipes", null) : Results.NoContent(),
        NutritionOperationStatus.NotFound => Results.NotFound(new { title = "Måltidet blev ikke fundet." }),
        NutritionOperationStatus.Conflict => Results.Conflict(new { title = "Måltidet blev ændret i en anden fane." }),
        _ => Invalid("Ernæring", "Kontrollér de indtastede oplysninger.")
    };
    private static IResult Invalid(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] }, title: "Kontrollér de indtastede oplysninger.");
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;
}
