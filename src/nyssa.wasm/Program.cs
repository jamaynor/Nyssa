using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Nyssa.Wasm;
using Nyssa.Wasm.Features.Authentication;
using Nyssa.Mcp.Client.Services;
using System.Net.Http.Json;
using System.Text.Json;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 1. Load appsettings.json from wwwroot at runtime
var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var configStream = await http.GetStreamAsync("appsettings.json");

// 2. Add the loaded config to the configuration system
builder.Configuration.AddJsonStream(configStream);

// 3. Register OidcConfiguration with the options pattern (correct for Blazor WASM)
builder.Services.Configure<OidcConfiguration>(section => builder.Configuration.GetSection("Oidc").Bind(section));

// 4. Authentication Services
builder.Services.AddScoped<AuthenticationStateService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped<OidcAuthenticationService>();

// 5. Authorization Services
builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();

// 6. HTTP Client Services
builder.Services.AddHttpClient<McpAuthenticationService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

var app = builder.Build();

// Initialize authentication state on startup
var authStateService = app.Services.GetRequiredService<AuthenticationStateService>();
await authStateService.InitializeAsync();

await app.RunAsync();
