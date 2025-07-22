using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure OIDC settings
builder.Services.Configure<OidcConfiguration>(
    builder.Configuration.GetSection("Oidc"));

// Add services
builder.Services.AddHttpClient<WorkOSAuthenticationService>();
builder.Services.AddScoped<WorkOSAuthenticationService>();

// Add CORS for WASM
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://localhost:7129") // WASM app origin
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure middleware
app.UseCors();

// API endpoints
app.MapGet("/api/auth/url", async (WorkOSAuthenticationService authService) =>
{
    try
    {
        var authUrl = authService.BuildAuthorizationUrl();
        return Results.Ok(new { authUrl });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/auth/exchange", async (HttpRequest request, WorkOSAuthenticationService authService) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        
        // The body is just the code string (sent via PostAsJsonAsync)
        var code = System.Text.Json.JsonSerializer.Deserialize<string>(body);
        
        if (string.IsNullOrEmpty(code))
        {
            return Results.BadRequest(new { error = "Authorization code is required" });
        }

        var result = await authService.ExchangeCodeForTokenAsync(code);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
