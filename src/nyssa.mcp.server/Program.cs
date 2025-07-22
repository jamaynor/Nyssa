using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/nyssa-mcp-server-.log", 
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Configure OIDC settings
builder.Services.Configure<OidcConfiguration>(options =>
{
    builder.Configuration.GetSection("Oidc").Bind(options);
    
    // Log all configuration attempts
    var logger = builder.Services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("Configuration");
    
    logger?.LogInformation("=== CONFIGURATION DEBUG ===");
    logger?.LogInformation("Raw Oidc section values:");
    logger?.LogInformation("  Authority: {Authority}", builder.Configuration["Oidc:Authority"]);
    logger?.LogInformation("  ClientId: {ClientId}", builder.Configuration["Oidc:ClientId"]);
    logger?.LogInformation("  RedirectUri: {RedirectUri}", builder.Configuration["Oidc:RedirectUri"]);
    logger?.LogInformation("  PostLogoutRedirectUri: {PostLogoutRedirectUri}", builder.Configuration["Oidc:PostLogoutRedirectUri"]);
    logger?.LogInformation("  Scope: {Scope}", builder.Configuration["Oidc:Scope"]);
    logger?.LogInformation("  ApiKey from Oidc:ApiKey: '{ApiKey}'", builder.Configuration["Oidc:ApiKey"] ?? "NULL");
    
    logger?.LogInformation("Alternative API key sources:");
    logger?.LogInformation("  WorkOS:ApiKey: '{WorkOSApiKey}'", builder.Configuration["WorkOS:ApiKey"] ?? "NULL");
    logger?.LogInformation("  WORKOS_API_KEY env var: '{EnvApiKey}'", Environment.GetEnvironmentVariable("WORKOS_API_KEY") ?? "NULL");
    logger?.LogInformation("  WORKOS_API_KEY config: '{ConfigApiKey}'", builder.Configuration["WORKOS_API_KEY"] ?? "NULL");
    logger?.LogInformation("  Oidc__ApiKey env var: '{OidcEnvApiKey}'", Environment.GetEnvironmentVariable("Oidc__ApiKey") ?? "NULL");
    
    // Get API key from multiple sources (in order of precedence)
    var apiKey = builder.Configuration["Oidc:ApiKey"] ??
                 builder.Configuration["WorkOS:ApiKey"] ?? 
                 Environment.GetEnvironmentVariable("WORKOS_API_KEY") ?? 
                 Environment.GetEnvironmentVariable("Oidc__ApiKey") ??
                 builder.Configuration["WORKOS_API_KEY"] ?? "";
    
    options.ApiKey = apiKey;
    
    logger?.LogInformation("Final resolved ApiKey: '{FinalApiKey}' (length: {Length})", 
        string.IsNullOrEmpty(apiKey) ? "EMPTY" : $"{apiKey[..Math.Min(10, apiKey.Length)]}...", 
        apiKey?.Length ?? 0);
    logger?.LogInformation("=== END CONFIGURATION DEBUG ===");
});

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
app.MapGet("/api/auth/url", async (WorkOSAuthenticationService authService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("=== AUTHORIZATION URL REQUEST ===");
        logger.LogInformation("Building WorkOS authorization URL");
        
        var authUrl = authService.BuildAuthorizationUrl();
        
        logger.LogInformation("Authorization URL generated successfully: {AuthUrl}", authUrl);
        logger.LogInformation("=== AUTHORIZATION URL RESPONSE ===");
        
        return Results.Ok(new { authUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate authorization URL: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

var exchangeRequestCount = 0;

app.MapPost("/api/auth/exchange", async (HttpRequest request, WorkOSAuthenticationService authService, ILogger<Program> logger) =>
{
    var currentRequest = Interlocked.Increment(ref exchangeRequestCount);
    
    try
    {
        logger.LogInformation("=== TOKEN EXCHANGE REQUEST #{RequestNumber} ===", currentRequest);
        logger.LogInformation("Received token exchange request #{RequestNumber}", currentRequest);
        logger.LogInformation("Content-Type: {ContentType}", request.ContentType);
        logger.LogInformation("Content-Length: {ContentLength}", request.ContentLength);
        
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        
        logger.LogInformation("Request body: {Body}", body);
        
        // The body is just the code string (sent via PostAsJsonAsync)
        var code = System.Text.Json.JsonSerializer.Deserialize<string>(body);
        
        logger.LogInformation("Extracted authorization code: {Code}", code);
        
        if (string.IsNullOrEmpty(code))
        {
            logger.LogWarning("Authorization code is null or empty");
            return Results.BadRequest(new { error = "Authorization code is required" });
        }

        logger.LogInformation("Calling WorkOS authentication service to exchange code");
        var result = await authService.ExchangeCodeForTokenAsync(code);
        
        logger.LogInformation("Token exchange result - Success: {IsSuccess}, User: {UserId}, Token Length: {TokenLength}", 
            result.IsSuccess, 
            result.User?.Id ?? "null", 
            result.AccessToken?.Length ?? 0);
            
        if (!result.IsSuccess)
        {
            logger.LogWarning("Token exchange failed: {Error}", result.ErrorMessage);
        }
        
        logger.LogInformation("=== TOKEN EXCHANGE RESPONSE ===");
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Token exchange endpoint failed: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
