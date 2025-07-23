using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Services;
using Nyssa.Mcp.Server.Configuration;
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

// Add MassTransit message bus
builder.Services.AddRbacMassTransit(builder.Configuration);
builder.Services.AddRbacMessageClients();

// Add PostgreSQL database services
builder.Services.AddRbacDatabase(builder.Configuration);

// Add JWT service for scoped token generation
builder.Services.Configure<Nyssa.Mcp.Server.Services.JwtOptions>(
    builder.Configuration.GetSection(Nyssa.Mcp.Server.Services.JwtOptions.SectionName));
builder.Services.AddSingleton<Nyssa.Mcp.Server.Services.IJwtService>(serviceProvider =>
{
    var options = builder.Configuration.GetSection(Nyssa.Mcp.Server.Services.JwtOptions.SectionName)
        .Get<Nyssa.Mcp.Server.Services.JwtOptions>() ?? new();
    var logger = serviceProvider.GetRequiredService<ILogger<Nyssa.Mcp.Server.Services.JwtService>>();
    return new Nyssa.Mcp.Server.Services.JwtService(options, logger);
});

// Add existing services
builder.Services.AddHttpClient<WorkOSAuthenticationService>();
builder.Services.AddScoped<WorkOSAuthenticationService>();

// Add enhanced authentication service
builder.Services.AddScoped<IEnhancedAuthenticationService, EnhancedAuthenticationService>();

// Add MCP authorization services
builder.Services.AddSingleton<Nyssa.Mcp.Server.Authorization.IMcpAuthorizationService, Nyssa.Mcp.Server.Authorization.McpAuthorizationService>();
builder.Services.AddSingleton<Nyssa.Mcp.Server.Authorization.McpAuthorizationMiddleware>();

// Add MCP server and tools
builder.Services.AddSingleton<Nyssa.Mcp.Server.Tools.EnhancedAuthenticationTools>();

// Add session services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

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

// Initialize MCP tools with services using a scope
using (var scope = app.Services.CreateScope())
{
    var authService = scope.ServiceProvider.GetRequiredService<IEnhancedAuthenticationService>();
    var authorizationService = scope.ServiceProvider.GetRequiredService<Nyssa.Mcp.Server.Authorization.IMcpAuthorizationService>();
    var toolsLogger = scope.ServiceProvider.GetRequiredService<ILogger<Nyssa.Mcp.Server.Tools.EnhancedAuthenticationTools>>();

    // Initialize enhanced authentication tools
    Nyssa.Mcp.Server.Tools.EnhancedAuthenticationTools.Initialize(authService, authorizationService, toolsLogger);

    // Initialize legacy authentication tools for backward compatibility
    var legacyAuthService = scope.ServiceProvider.GetRequiredService<WorkOSAuthenticationService>();
    Nyssa.Mcp.Server.Tools.AuthenticationTools.Initialize(legacyAuthService);
}

// Configure middleware
app.UseCors();
app.UseSession();

// API endpoints
app.MapGet("/api/auth/url", (IEnhancedAuthenticationService authService, ILogger<Program> logger) =>
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

app.MapPost("/api/auth/exchange", async (HttpRequest request, IEnhancedAuthenticationService authService, ILogger<Program> logger) =>
{
    var currentRequest = Interlocked.Increment(ref exchangeRequestCount);
    
    try
    {
        logger.LogInformation("=== ENHANCED TOKEN EXCHANGE REQUEST #{RequestNumber} ===", currentRequest);
        logger.LogInformation("Received enhanced token exchange request #{RequestNumber}", currentRequest);
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

        // Extract client information for audit logging
        var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = request.Headers.UserAgent.ToString();
        var sessionId = request.HttpContext.Session?.Id ?? Guid.NewGuid().ToString();

        logger.LogInformation("Calling enhanced authentication service to exchange code and generate scoped token");
        var result = await authService.AuthenticateWithScopedTokenAsync(code, ipAddress, userAgent, sessionId);
        
        if (!result.Success)
        {
            logger.LogWarning("Enhanced authentication failed: {Errors}", string.Join(", ", result.Errors.Select(e => e.Text)));
            return Results.BadRequest(new { 
                error = result.Errors.FirstOrDefault()?.UserFriendlyText ?? "Authentication failed",
                code = result.Errors.FirstOrDefault()?.Code ?? 4000
            });
        }

        var authResult = result.Value;
        logger.LogInformation("Enhanced authentication successful - User: {UserId}, IsNew: {IsNewUser}, Permissions: {PermissionCount}, Token Expires: {ExpiresAt}",
            authResult.User.Id,
            authResult.IsNewUser,
            authResult.Permissions.Permissions.Count,
            authResult.ScopedToken.ExpiresAt);
        
        logger.LogInformation("=== ENHANCED TOKEN EXCHANGE RESPONSE ===");
        
        // Return the scoped token and minimal user info
        return Results.Ok(new 
        {
            access_token = authResult.ScopedToken.Token,
            token_type = "Bearer",
            expires_in = (int)(authResult.ScopedToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
            expires_at = authResult.ScopedToken.ExpiresAt,
            user = new
            {
                id = authResult.User.Id,
                external_id = authResult.User.ExternalId,
                email = authResult.User.Email,
                first_name = authResult.User.FirstName,
                last_name = authResult.User.LastName,
                name = $"{authResult.User.FirstName} {authResult.User.LastName}".Trim()
            },
            organization = new 
            {
                id = authResult.PrimaryOrganization.OrganizationId,
                name = authResult.PrimaryOrganization.OrganizationName,
                path = authResult.PrimaryOrganization.OrganizationPath
            },
            permissions = authResult.Permissions.PermissionCodes,
            roles = authResult.Permissions.Roles.Select(r => new { id = r.RoleId, name = r.RoleName }),
            is_new_user = authResult.IsNewUser,
            is_success = true
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Enhanced token exchange endpoint failed: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Token validation endpoint
app.MapPost("/api/auth/validate", (HttpRequest request, IEnhancedAuthenticationService authService, ILogger<Program> logger) =>
{
    try
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return Results.Unauthorized();
        }

        var token = authHeader["Bearer ".Length..];
        var validationResult = authService.ValidateScopedToken(token);

        if (!validationResult.Success)
        {
            logger.LogWarning("Token validation failed: {Errors}", string.Join(", ", validationResult.Errors.Select(e => e.Text)));
            return Results.Unauthorized();
        }

        var payload = validationResult.Value;
        logger.LogInformation("Token validated successfully for user {UserId}", payload.User.Id);

        return Results.Ok(new
        {
            valid = true,
            user = payload.User,
            organization = payload.Organization,
            permissions = payload.Permissions,
            roles = payload.Roles,
            expires_at = DateTimeOffset.FromUnixTimeSeconds(payload.Exp).DateTime,
            includes_inherited = payload.IncludesInherited
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Token validation endpoint failed: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Token revocation endpoint
app.MapPost("/api/auth/revoke", async (HttpRequest request, IEnhancedAuthenticationService authService, ILogger<Program> logger) =>
{
    try
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return Results.Unauthorized();
        }

        var token = authHeader["Bearer ".Length..];
        var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        
        var result = await authService.RevokeScopedTokenAsync(token, "user_logout", ipAddress: ipAddress);

        if (!result.Success)
        {
            logger.LogWarning("Token revocation failed: {Errors}", string.Join(", ", result.Errors.Select(e => e.Text)));
            return Results.BadRequest(new { error = "Failed to revoke token" });
        }

        logger.LogInformation("Token revoked successfully");
        return Results.Ok(new { revoked = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Token revocation endpoint failed: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
