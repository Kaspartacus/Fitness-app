using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FitnessApp.Application.Authentication;
using FitnessApp.Application.Settings;
using FitnessApp.Contracts.Settings;

namespace FitnessApp.Server.Settings;

internal static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings").RequireAuthorization();

        group.MapGet("", GetOverviewAsync);
        group.MapPut("/profile", UpdateProfileAsync);
        group.MapPut("/nutrition-goals", UpdateNutritionGoalsAsync);
        group.MapPut("/notification-preferences", UpdateNotificationPreferencesAsync);
        group.MapPost("/garmin-demo/connect", ConnectGarminDemoAsync);
        group.MapPost("/garmin-demo/disconnect", DisconnectGarminDemoAsync);
        group.MapGet("/notifications", GetNotificationsAsync);
        group.MapPost("/notifications/{notificationId:guid}/read", MarkNotificationReadAsync);

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var overview = await settingsService.GetOverviewAsync(
            Owner(user),
            IsAdministrator(user),
            cancellationToken);
        return overview is null
            ? Results.NotFound(new { title = "Indstillingerne blev ikke fundet." })
            : Results.Ok(Map(overview));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileSettingsRequest? request,
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ProfileValidation();
        }

        var result = await settingsService.UpdateProfileAsync(
            Owner(user),
            new UpdateProfileSettingsInput(
                request.DisplayName,
                request.HeightCm,
                request.WeightKg),
            cancellationToken);

        return result.Status switch
        {
            SettingsSaveStatus.Saved when result.Profile is { } profile => Results.Ok(Map(profile)),
            SettingsSaveStatus.Invalid => ProfileValidation(),
            SettingsSaveStatus.NotFound => Results.NotFound(new { title = "Profilen blev ikke fundet." }),
            _ => Unexpected()
        };
    }

    private static async Task<IResult> UpdateNutritionGoalsAsync(
        UpdateNutritionGoalsRequest? request,
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return NutritionGoalsValidation();
        }

        var result = await settingsService.UpdateNutritionGoalsAsync(
            Owner(user),
            new UpdateNutritionGoalsInput(
                request.DailyCaloriesTarget,
                request.ProteinTargetGrams,
                request.CarbohydrateTargetGrams,
                request.FatTargetGrams,
                request.SugarTargetGrams),
            cancellationToken);

        return result.Status switch
        {
            SettingsSaveStatus.Saved when result.Goals is { } goals => Results.Ok(Map(goals)),
            SettingsSaveStatus.Invalid => NutritionGoalsValidation(),
            SettingsSaveStatus.NotFound => Results.NotFound(new { title = "Ernæringsmålene blev ikke fundet." }),
            _ => Unexpected()
        };
    }

    private static async Task<IResult> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesRequest? request,
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Notifikationer"] = ["Angiv notifikationsindstillingerne, og prøv igen."]
            }, title: "Kontrollér notifikationsindstillingerne.");
        }

        var result = await settingsService.UpdateNotificationPreferencesAsync(
            Owner(user),
            IsAdministrator(user),
            new UpdateNotificationPreferencesInput(
                request.TrainingRemindersEnabled,
                request.AdminRequestNotificationsEnabled),
            cancellationToken);

        return result.Status switch
        {
            SettingsSaveStatus.Saved when result.Preferences is { } preferences => Results.Ok(Map(preferences)),
            SettingsSaveStatus.Invalid => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Notifikationer"] = ["Kontrollér notifikationsindstillingerne."]
            }),
            SettingsSaveStatus.NotFound => Results.NotFound(new { title = "Indstillingerne blev ikke fundet." }),
            _ => Unexpected()
        };
    }

    private static async Task<IResult> ConnectGarminDemoAsync(
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken) =>
        GarminStatus(await settingsService.ConnectGarminDemoAsync(Owner(user), cancellationToken));

    private static async Task<IResult> DisconnectGarminDemoAsync(
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken) =>
        GarminStatus(await settingsService.DisconnectGarminDemoAsync(Owner(user), cancellationToken));

    private static async Task<IResult> GetNotificationsAsync(
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var notifications = await settingsService.GetNotificationsAsync(
            Owner(user),
            IsAdministrator(user),
            cancellationToken);
        return Results.Ok(new InAppNotificationListResponse(notifications.Select(Map).ToArray()));
    }

    private static async Task<IResult> MarkNotificationReadAsync(
        Guid notificationId,
        ClaimsPrincipal user,
        ISettingsService settingsService,
        CancellationToken cancellationToken) =>
        await settingsService.MarkNotificationReadAsync(Owner(user), IsAdministrator(user), notificationId, cancellationToken)
            is NotificationReadStatus.Read
                ? Results.NoContent()
                : Results.NotFound(new { title = "Notifikationen blev ikke fundet." });

    private static IResult GarminStatus(GarminMockConnectionResult result) => result.Status switch
    {
        SettingsSaveStatus.Saved when result.Connection is { } connection => Results.Ok(Map(connection)),
        SettingsSaveStatus.NotFound => Results.NotFound(new { title = "Indstillingerne blev ikke fundet." }),
        _ => Unexpected()
    };

    private static IResult ProfileValidation() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["Profil"] = ["Kontrollér navn, højde og vægt."]
    }, title: "Kontrollér profiloplysningerne.");

    private static IResult NutritionGoalsValidation() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["Ernæringsmål"] = ["Kontrollér energi- og makromål."]
    }, title: "Kontrollér ernæringsmålene.");

    private static IResult Unexpected() => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: "Der opstod en uventet fejl.");

    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    private static bool IsAdministrator(ClaimsPrincipal user) => user.IsInRole(AuthenticationConstants.AdminRole);

    private static SettingsOverviewResponse Map(SettingsOverviewData overview) => new(
        Map(overview.Profile),
        Map(overview.NotificationPreferences),
        Map(overview.Garmin),
        overview.UnreadNotificationCount);

    private static ProfileSettingsResponse Map(ProfileSettingsData profile) => new(
        profile.DisplayName,
        profile.HeightCm,
        profile.WeightKg,
        Map(profile.NutritionGoals));

    private static NutritionGoalsResponse Map(NutritionGoalsData goals) => new(
        goals.DailyCaloriesTarget,
        goals.ProteinTargetGrams,
        goals.CarbohydrateTargetGrams,
        goals.FatTargetGrams,
        goals.SugarTargetGrams);

    private static NotificationPreferencesResponse Map(NotificationPreferencesData preferences) => new(
        preferences.TrainingRemindersEnabled,
        preferences.AdminRequestNotificationsEnabled,
        preferences.IsAdministrator);

    private static GarminMockConnectionResponse Map(GarminMockConnectionData connection) => new(
        connection.IsConnected,
        connection.ConnectedAt);

    private static InAppNotificationResponse Map(InAppNotificationData notification) => new(
        notification.Id,
        notification.Kind,
        notification.Title,
        notification.Message,
        notification.TargetPath,
        notification.CreatedAt,
        notification.ReadAt);
}
