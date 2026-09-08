using FitnessApp.Client;
using FitnessApp.Client.Authentication;
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
builder.Services.AddHttpClient(AuthenticationClient.ClientName, client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<ApiAuthorizationHandler>();
builder.Services.AddScoped<AuthenticationClient>();
builder.Services.AddScoped<RegistrationClient>();
builder.Services.AddScoped<UserAdministrationClient>();

await builder.Build().RunAsync();
