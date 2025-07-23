using System.Reflection;
using System.Text.Json;
using MCPSharp;
using Nyssa.Mcp.Server.Models;

namespace Nyssa.Mcp.Server.Authorization
{
    /// <summary>
    /// Middleware that intercepts MCP tool calls and performs authorization checks
    /// </summary>
    public class McpAuthorizationMiddleware
    {
        private readonly IMcpAuthorizationService _authorizationService;
        private readonly ILogger<McpAuthorizationMiddleware> _logger;

        public McpAuthorizationMiddleware(
            IMcpAuthorizationService authorizationService,
            ILogger<McpAuthorizationMiddleware> logger)
        {
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Intercepts and authorizes MCP tool calls
        /// </summary>
        public async Task<string> InterceptToolCallAsync(
            MethodInfo toolMethod,
            object toolInstance,
            object[] parameters,
            string? authorizationToken = null,
            Dictionary<string, object>? additionalContext = null)
        {
            try
            {
                var toolName = toolMethod.Name;
                _logger.LogDebug("Intercepting MCP tool call: {ToolName}", toolName);

                // Perform authorization check
                var authResult = await _authorizationService.AuthorizeToolAsync(
                    toolMethod,
                    authorizationToken,
                    additionalContext);

                if (!authResult.Success)
                {
                    _logger.LogWarning("Authorization failed for tool {ToolName}: {Errors}",
                        toolName, string.Join(", ", authResult.Errors.Select(e => e.Text)));

                    return CreateAuthorizationErrorResponse(authResult.Errors);
                }

                var authContext = authResult.Value;

                // Log successful authorization
                if (authContext.IsAuthenticated)
                {
                    _logger.LogInformation("Tool {ToolName} authorized for user {UserId} with permissions: {Permissions}",
                        toolName, authContext.User?.Id, string.Join(", ", authContext.Permissions.Take(5)));
                }
                else
                {
                    _logger.LogDebug("Tool {ToolName} accessed anonymously", toolName);
                }

                // Execute the tool method
                var result = await ExecuteToolMethodAsync(toolMethod, toolInstance, parameters);
                
                // Log tool execution
                _logger.LogDebug("Tool {ToolName} executed successfully for user {UserId}",
                    toolName, authContext.User?.Id ?? "anonymous");

                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access to tool {ToolName}: {Message}",
                    toolMethod.Name, ex.Message);
                return CreateAuthorizationErrorResponse(RbacErrors.Authorization.InsufficientPermissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool execution failed for {ToolName}: {Message}",
                    toolMethod.Name, ex.Message);
                return CreateExecutionErrorResponse(ex);
            }
        }

        /// <summary>
        /// Creates an authorized tool call delegate that includes authorization checking
        /// </summary>
        public Func<object[], Task<string>> CreateAuthorizedToolDelegate(
            MethodInfo toolMethod,
            object toolInstance,
            Func<string?> getAuthorizationToken)
        {
            return async (parameters) =>
            {
                var authToken = getAuthorizationToken?.Invoke();
                return await InterceptToolCallAsync(toolMethod, toolInstance, parameters, authToken);
            };
        }

        /// <summary>
        /// Extracts authorization token from MCP tool parameters
        /// </summary>
        public static string? ExtractAuthorizationToken(object[] parameters, MethodInfo method)
        {
            try
            {
                // Look for parameters named "authToken", "authorizationToken", "token", or "authorization"
                var parameterInfo = method.GetParameters();
                
                for (int i = 0; i < parameterInfo.Length && i < parameters.Length; i++)
                {
                    var param = parameterInfo[i];
                    var paramName = param.Name?.ToLowerInvariant();
                    
                    if (paramName == "authtoken" || 
                        paramName == "authorizationtoken" || 
                        paramName == "token" ||
                        paramName == "authorization")
                    {
                        var tokenValue = parameters[i]?.ToString();
                        if (IsLikelyJwtToken(tokenValue))
                        {
                            return tokenValue;
                        }
                    }
                }

                // Look for Bearer token in first string parameter that looks like a JWT
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i] is string strParam && IsLikelyJwtToken(strParam))
                    {
                        return strParam;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a standardized error response for authorization failures
        /// </summary>
        private string CreateAuthorizationErrorResponse(params ErrorMessage[] errors)
        {
            var error = errors.FirstOrDefault() ?? RbacErrors.Authorization.InsufficientPermissions;
            
            return JsonSerializer.Serialize(new
            {
                success = false,
                authorized = false,
                error = error.UserFriendlyText,
                error_code = error.Code,
                error_type = "authorization_error",
                details = error.Text
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        /// <summary>
        /// Creates a standardized error response for authorization failures
        /// </summary>
        private string CreateAuthorizationErrorResponse(IEnumerable<ErrorMessage> errors)
        {
            return CreateAuthorizationErrorResponse(errors.ToArray());
        }

        /// <summary>
        /// Creates a standardized error response for execution failures
        /// </summary>
        private string CreateExecutionErrorResponse(Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool execution failed",
                error_type = "execution_error",
                details = ex.Message
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        /// <summary>
        /// Executes the actual tool method
        /// </summary>
        private async Task<string> ExecuteToolMethodAsync(MethodInfo method, object instance, object[] parameters)
        {
            try
            {
                var result = method.Invoke(instance, parameters);

                // Handle async methods
                if (result is Task<string> taskString)
                {
                    return await taskString;
                }
                else if (result is Task task)
                {
                    await task;
                    return JsonSerializer.Serialize(new { success = true, message = "Operation completed" });
                }
                else if (result is string str)
                {
                    return str;
                }
                else
                {
                    return JsonSerializer.Serialize(result ?? new { success = true });
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Checks if a string looks like a JWT token
        /// </summary>
        private static bool IsLikelyJwtToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            // Remove Bearer prefix if present
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token["Bearer ".Length..];

            // JWT tokens have 3 parts separated by dots
            var parts = token.Split('.');
            if (parts.Length != 3)
                return false;

            // Each part should be base64-like (no spaces, minimum length of 4 for test tokens)
            return parts.All(part => 
                !string.IsNullOrWhiteSpace(part) && 
                part.Length >= 4 && 
                part.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '='));
        }
    }

    /// <summary>
    /// Extension methods for easier MCP authorization integration
    /// </summary>
    public static class McpAuthorizationExtensions
    {
        /// <summary>
        /// Checks if a method requires authentication
        /// </summary>
        public static bool RequiresAuthentication(this MethodInfo method)
        {
            return method.GetCustomAttribute<McpRequireAuthenticationAttribute>() != null ||
                   method.GetCustomAttributes<McpRequirePermissionAttribute>().Any() ||
                   method.GetCustomAttribute<McpRequireRoleAttribute>() != null ||
                   method.GetCustomAttribute<McpRequireOrganizationAttribute>() != null;
        }

        /// <summary>
        /// Checks if a method allows anonymous access
        /// </summary>
        public static bool AllowsAnonymousAccess(this MethodInfo method)
        {
            return method.GetCustomAttribute<McpAllowAnonymousAttribute>() != null;
        }

        /// <summary>
        /// Gets a summary of authorization requirements for a method
        /// </summary>
        public static string GetAuthorizationSummary(this MethodInfo method)
        {
            var requirements = new List<string>();

            if (method.AllowsAnonymousAccess())
            {
                return "Anonymous access allowed";
            }

            if (method.RequiresAuthentication())
            {
                requirements.Add("Authentication required");
            }

            var permissions = method.GetCustomAttributes<McpRequirePermissionAttribute>()
                .Select(attr => attr.Permission).ToList();
            if (permissions.Any())
            {
                requirements.Add($"Permissions: {string.Join(", ", permissions)}");
            }

            var roles = method.GetCustomAttributes<McpRequireRoleAttribute>()
                .Select(attr => attr.RoleName).ToList();
            if (roles.Any())
            {
                requirements.Add($"Roles: {string.Join(", ", roles)}");
            }

            var orgReq = method.GetCustomAttribute<McpRequireOrganizationAttribute>();
            if (orgReq != null)
            {
                if (!string.IsNullOrEmpty(orgReq.OrganizationId))
                {
                    requirements.Add($"Organization: {orgReq.OrganizationId}");
                }
                else
                {
                    requirements.Add("Organization membership required");
                }
            }

            return requirements.Any() ? string.Join("; ", requirements) : "No special requirements";
        }
    }
}