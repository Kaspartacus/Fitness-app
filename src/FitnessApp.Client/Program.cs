using FitnessApp.Client;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Calendar;
using FitnessApp.Client.Nutrition;
using FitnessApp.Client.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<InMemoryAuthenticationStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(services =>
    services.GetRequiredService<InMemoryAuthenticationStateProvider>());
builder.Services.AddTransient<ApiAuthorizationHandler>();
builder.Services.AddHttpClient("FitnessAppSession", client =>
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddHttpClient(AuthenticationClient.ClientName, client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<ApiAuthorizationHandler>();
builder.Services.AddSingleton<AuthenticationClient>();
builder.Services.AddScoped<RegistrationClient>();
builder.Services.AddScoped<PasswordResetClient>();
builder.Services.AddScoped<UserAdministrationClient>();
builder.Services.AddScoped<FitnessApp.Client.Strength.StrengthProgramClient>();
builder.Services.AddScoped<FitnessApp.Client.Running.RunningClient>();
builder.Services.AddScoped<CalendarClient>();
builder.Services.AddScoped<NutritionClient>();
builder.Services.AddScoped<SettingsClient>();
builder.Services.AddScoped<FitnessApp.Client.Diary.DiaryClient>();
builder.Services.AddScoped<ThemePreference>();
builder.Services.AddScoped<FitnessApp.Client.Notifications.NotificationClient>();

await builder.Build().RunAsync();
