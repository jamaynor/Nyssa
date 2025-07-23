using MCPSharp;
using Nyssa.Mcp.Server.Authorization;
using Nyssa.Mcp.Server.Services;
using System.Reflection;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Tools
{
    /// <summary>
    /// Enhanced authentication tools with RBAC authorization support
    /// </summary>
    public class EnhancedAuthenticationTools
    {
        private static IEnhancedAuthenticationService? _authService;
        private static IMcpAuthorizationService? _authorizationService;
        private static ILogger? _logger;

        public EnhancedAuthenticationTools()
        {
        }

        /// <summary>
        /// Initialize the authentication tools with required services
        /// </summary>
        public static void Initialize(
            IEnhancedAuthenticationService authService,
            IMcpAuthorizationService authorizationService,
            ILogger logger)
        {
            _authService = authService;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        /// <summary>
        /// Exchange authorization code for scoped access token with embedded permissions
        /// </summary>
        [McpTool("exchange_code_for_scoped_token", "Exchange authorization code for scoped access token with RBAC permissions")]
        [McpAllowAnonymous("Authentication endpoint that creates tokens")]
        public async Task<string> ExchangeCodeForScopedTokenAsync(
            [McpParameter(true, "Authorization code from WorkOS")] string code,
            [McpParameter(false, "Client IP address for audit logging")] string? ipAddress = null,
            [McpParameter(false, "User agent for audit logging")] string? userAgent = null)
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                _logger?.LogInformation("Processing scoped token exchange for code");

                var result = await _authService.AuthenticateWithScopedTokenAsync(
                    code, 
                    ipAddress, 
                    userAgent, 
                    Guid.NewGuid().ToString());

                if (!result.Success)
                {
                    var error = result.Errors.FirstOrDefault();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = error?.UserFriendlyText ?? "Authentication failed",
                        error_code = error?.Code ?? 4000
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                var authResult = result.Value;

                return JsonSerializer.Serialize(new
                {
                    success = true,
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
                    is_new_user = authResult.IsNewUser
                }, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Enhanced token exchange failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get WorkOS authorization URL for user login
        /// </summary>
        [McpTool("get_auth_url", "Get WorkOS authorization URL for user login")]
        [McpAllowAnonymous("Public endpoint for initiating authentication")]
        public string GetAuthUrl()
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                var authUrl = _authService.BuildAuthorizationUrl();
                return JsonSerializer.Serialize(new { success = true, authUrl }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate authorization URL: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Validate a scoped token and return user information
        /// </summary>
        [McpTool("validate_token", "Validate a scoped token and return user information")]
        [McpAllowAnonymous("Token validation endpoint")]
        public string ValidateToken(
            [McpParameter(true, "JWT token to validate")] string token)
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                var result = _authService.ValidateScopedToken(token);

                if (!result.Success)
                {
                    var error = result.Errors.FirstOrDefault();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        valid = false,
                        error = error?.UserFriendlyText ?? "Token validation failed",
                        error_code = error?.Code ?? 4001
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                var payload = result.Value;

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    valid = true,
                    user = payload.User,
                    organization = payload.Organization,
                    permissions = payload.Permissions,
                    roles = payload.Roles,
                    expires_at = DateTimeOffset.FromUnixTimeSeconds(payload.Exp).DateTime,
                    includes_inherited = payload.IncludesInherited,
                    token_metadata = payload.Metadata
                }, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Token validation failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, valid = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Revoke a scoped token (blacklist it)
        /// </summary>
        [McpTool("revoke_token", "Revoke a scoped token")]
        [McpRequireAuthentication("Token revocation requires authentication")]
        public async Task<string> RevokeTokenAsync(
            [McpParameter(true, "JWT token to revoke")] string token,
            [McpParameter(false, "Reason for revocation")] string reason = "user_logout",
            [McpParameter(false, "IP address for audit logging")] string? ipAddress = null)
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                var result = await _authService.RevokeScopedTokenAsync(token, reason, ipAddress: ipAddress);

                if (!result.Success)
                {
                    var error = result.Errors.FirstOrDefault();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = error?.UserFriendlyText ?? "Token revocation failed",
                        error_code = error?.Code ?? 4106
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    revoked = true,
                    message = "Token has been successfully revoked"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Token revocation failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get current user's permissions and organization info
        /// </summary>
        [McpTool("get_user_context", "Get current user's permissions and organization info")]
        [McpRequireAuthentication("User context requires authentication")]
        public async Task<string> GetUserContextAsync(
            [McpParameter(true, "Authorization token")] string authToken)
        {
            try
            {
                if (_authorizationService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authorization service not initialized" });

                // Create authorization context
                var contextResult = _authorizationService.CreateAuthorizationContext(authToken);
                if (!contextResult.Success)
                {
                    var error = contextResult.Errors.FirstOrDefault();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = error?.UserFriendlyText ?? "Failed to get user context",
                        error_code = error?.Code ?? 4001
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                var context = contextResult.Value;

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    is_authenticated = context.IsAuthenticated,
                    user = context.User,
                    organization = context.Organization,
                    permissions = context.Permissions,
                    roles = context.Roles,
                    expires_at = context.ExpiresAt,
                    includes_inherited = context.IncludesInherited,
                    metadata = context.Metadata
                }, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Get user context failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Check if current user has specific permissions
        /// </summary>
        [McpTool("check_permissions", "Check if current user has specific permissions")]
        [McpRequireAuthentication("Permission checking requires authentication")]
        public async Task<string> CheckPermissionsAsync(
            [McpParameter(true, "Authorization token")] string authToken,
            [McpParameter(true, "Permissions to check (comma-separated)")] string permissions)
        {
            try
            {
                if (_authorizationService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authorization service not initialized" });

                var contextResult = _authorizationService.CreateAuthorizationContext(authToken);
                if (!contextResult.Success)
                {
                    var error = contextResult.Errors.FirstOrDefault();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = error?.UserFriendlyText ?? "Failed to check permissions",
                        error_code = error?.Code ?? 4001
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }

                var context = contextResult.Value;
                var permissionList = permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToList();

                var results = new Dictionary<string, bool>();
                foreach (var permission in permissionList)
                {
                    results[permission] = context.HasPermission(permission);
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    user_id = context.User?.Id,
                    checked_permissions = results,
                    has_all = results.Values.All(v => v),
                    has_any = results.Values.Any(v => v),
                    user_permissions = context.Permissions
                }, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Permission check failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Demo tool that requires specific permissions
        /// </summary>
        [McpTool("demo_protected_tool", "Demo tool that requires user management permissions")]
        [McpRequirePermission("users:read", Description = "Required to read user information")]
        [McpRequirePermission("users:write", Description = "Required to modify user information")]
        public async Task<string> DemoProtectedToolAsync(
            [McpParameter(true, "Authorization token")] string authToken,
            [McpParameter(false, "Demo parameter")] string? demoParam = null)
        {
            try
            {
                if (_authorizationService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authorization service not initialized" });

                // Manual authorization check since middleware interception is not set up
                var method = typeof(EnhancedAuthenticationTools).GetMethod(nameof(DemoProtectedToolAsync))!;
                var authResult = await _authorizationService.AuthorizeToolAsync(method, authToken, null);
                
                if (!authResult.Success)
                {
                    return JsonSerializer.Serialize(new 
                    { 
                        success = false, 
                        authorized = false,
                        error = authResult.Errors.FirstOrDefault()?.UserFriendlyText ?? "Authorization failed",
                        error_code = authResult.Errors.FirstOrDefault()?.Code ?? 4000,
                        error_type = "authorization_error"
                    });
                }

                var context = authResult.Value;

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Successfully accessed protected tool",
                    user = context.User?.Name,
                    permissions = context.Permissions,
                    demo_param = demoParam
                }, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Demo protected tool failed: {Message}", ex.Message);
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }
    }
}