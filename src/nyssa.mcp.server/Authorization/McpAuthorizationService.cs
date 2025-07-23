using System.Reflection;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Services;

namespace Nyssa.Mcp.Server.Authorization
{
    /// <summary>
    /// Service for handling MCP tool authorization
    /// </summary>
    public interface IMcpAuthorizationService
    {
        /// <summary>
        /// Authorizes an MCP tool method call
        /// </summary>
        Task<Result<McpAuthorizationContext>> AuthorizeToolAsync(
            MethodInfo toolMethod,
            string? authorizationToken = null,
            Dictionary<string, object>? parameters = null);

        /// <summary>
        /// Creates authorization context from a JWT token
        /// </summary>
        Result<McpAuthorizationContext> CreateAuthorizationContext(string? authorizationToken);

        /// <summary>
        /// Checks if a method has specific permission requirements
        /// </summary>
        bool HasPermissionRequirements(MethodInfo method);

        /// <summary>
        /// Gets all required permissions for a method
        /// </summary>
        List<string> GetRequiredPermissions(MethodInfo method);

        /// <summary>
        /// Gets all required roles for a method
        /// </summary>
        List<string> GetRequiredRoles(MethodInfo method);
    }

    /// <summary>
    /// Implementation of MCP authorization service
    /// </summary>
    public class McpAuthorizationService : IMcpAuthorizationService
    {
        private readonly IJwtService _jwtService;
        private readonly ILogger<McpAuthorizationService> _logger;

        public McpAuthorizationService(
            IJwtService jwtService,
            ILogger<McpAuthorizationService> logger)
        {
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<McpAuthorizationContext>> AuthorizeToolAsync(
            MethodInfo toolMethod,
            string? authorizationToken = null,
            Dictionary<string, object>? parameters = null)
        {
            try
            {
                _logger.LogDebug("Authorizing MCP tool: {ToolName}", toolMethod.Name);

                // Check if anonymous access is explicitly allowed
                if (toolMethod.GetCustomAttribute<McpAllowAnonymousAttribute>() != null)
                {
                    _logger.LogDebug("Tool {ToolName} allows anonymous access", toolMethod.Name);
                    return Result<McpAuthorizationContext>.Ok(new McpAuthorizationContext { IsAuthenticated = false });
                }

                // Create authorization context from token
                var contextResult = CreateAuthorizationContext(authorizationToken);
                if (!contextResult.Success)
                {
                    _logger.LogWarning("Failed to create authorization context for tool {ToolName}: {Errors}",
                        toolMethod.Name, string.Join(", ", contextResult.Errors.Select(e => e.Text)));
                    return Result<McpAuthorizationContext>.Fail(contextResult.Errors);
                }

                var context = contextResult.Value;

                // Check authentication requirement
                var requiresAuth = toolMethod.GetCustomAttribute<McpRequireAuthenticationAttribute>() != null ||
                                 HasPermissionRequirements(toolMethod) ||
                                 toolMethod.GetCustomAttribute<McpRequireRoleAttribute>() != null ||
                                 toolMethod.GetCustomAttribute<McpRequireOrganizationAttribute>() != null;

                if (requiresAuth && !context.IsAuthenticated)
                {
                    _logger.LogWarning("Tool {ToolName} requires authentication but no valid token provided", toolMethod.Name);
                    return Result<McpAuthorizationContext>.Fail(RbacErrors.Authentication.InvalidToken);
                }

                // If not authenticated but no requirements, allow access
                if (!context.IsAuthenticated)
                {
                    return Result<McpAuthorizationContext>.Ok(context);
                }

                // Check permission requirements
                var permissionCheckResult = await CheckPermissionRequirementsAsync(toolMethod, context);
                if (!permissionCheckResult.Success)
                {
                    return Result<McpAuthorizationContext>.Fail(permissionCheckResult.Errors);
                }

                // Check role requirements
                var roleCheckResult = CheckRoleRequirements(toolMethod, context);
                if (!roleCheckResult.Success)
                {
                    return Result<McpAuthorizationContext>.Fail(roleCheckResult.Errors);
                }

                // Check organization requirements
                var orgCheckResult = CheckOrganizationRequirements(toolMethod, context);
                if (!orgCheckResult.Success)
                {
                    return Result<McpAuthorizationContext>.Fail(orgCheckResult.Errors);
                }

                _logger.LogDebug("Authorization successful for tool {ToolName}, user {UserId}",
                    toolMethod.Name, context.User?.Id);

                return Result<McpAuthorizationContext>.Ok(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authorization failed for tool {ToolName}: {Message}",
                    toolMethod.Name, ex.Message);
                return Result<McpAuthorizationContext>.Fail(RbacErrors.Authorization.InsufficientPermissions);
            }
        }

        public Result<McpAuthorizationContext> CreateAuthorizationContext(string? authorizationToken)
        {
            if (string.IsNullOrWhiteSpace(authorizationToken))
            {
                return Result<McpAuthorizationContext>.Ok(new McpAuthorizationContext 
                { 
                    IsAuthenticated = false 
                });
            }

            // Remove "Bearer " prefix if present
            var token = authorizationToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorizationToken["Bearer ".Length..]
                : authorizationToken;

            // Validate the token
            var validationResult = _jwtService.ValidateToken(token);
            if (!validationResult.Success)
            {
                _logger.LogDebug("Token validation failed: {Errors}",
                    string.Join(", ", validationResult.Errors.Select(e => e.Text)));
                return Result<McpAuthorizationContext>.Fail(validationResult.Errors);
            }

            var payload = validationResult.Value;

            // Create authorization context
            var context = new McpAuthorizationContext
            {
                IsAuthenticated = true,
                User = new McpUser
                {
                    Id = payload.User.Id,
                    Email = payload.User.Email,
                    Name = payload.User.Name,
                    FirstName = payload.User.FirstName,
                    LastName = payload.User.LastName,
                    ExternalId = payload.User.ExternalId
                },
                Organization = new McpOrganization
                {
                    Id = payload.Organization.Id,
                    Name = payload.Organization.Name,
                    Path = payload.Organization.Path
                },
                Permissions = payload.Permissions,
                Roles = payload.Roles.Select(r => new McpRole
                {
                    Id = r.Id,
                    Name = r.Name,
                    IsInheritable = r.IsInheritable
                }).ToList(),
                Token = token,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp).DateTime,
                IncludesInherited = payload.IncludesInherited,
                Metadata = new Dictionary<string, object>
                {
                    ["jti"] = payload.Jti,
                    ["iat"] = payload.Iat,
                    ["scope"] = payload.Scope,
                    ["token_type"] = payload.TokenType,
                    ["generation_source"] = payload.Metadata.Source,
                    ["permission_count"] = payload.Metadata.PermissionCount,
                    ["inherited_count"] = payload.Metadata.InheritedPermissionCount
                }
            };

            return Result<McpAuthorizationContext>.Ok(context);
        }

        public bool HasPermissionRequirements(MethodInfo method)
        {
            return method.GetCustomAttributes<McpRequirePermissionAttribute>().Any();
        }

        public List<string> GetRequiredPermissions(MethodInfo method)
        {
            return method.GetCustomAttributes<McpRequirePermissionAttribute>()
                .Select(attr => attr.Permission)
                .ToList();
        }

        public List<string> GetRequiredRoles(MethodInfo method)
        {
            return method.GetCustomAttributes<McpRequireRoleAttribute>()
                .Select(attr => attr.RoleName)
                .ToList();
        }

        // Private helper methods

        private async Task<Result> CheckPermissionRequirementsAsync(MethodInfo method, McpAuthorizationContext context)
        {
            var permissionAttributes = method.GetCustomAttributes<McpRequirePermissionAttribute>().ToList();
            if (!permissionAttributes.Any())
            {
                return Result.Ok();
            }

            var missingPermissions = new List<string>();

            foreach (var attr in permissionAttributes)
            {
                if (!context.HasPermission(attr.Permission))
                {
                    if (attr.IsRequired)
                    {
                        missingPermissions.Add(attr.Permission);
                    }
                    else
                    {
                        _logger.LogDebug("User {UserId} missing recommended permission {Permission} for tool {ToolName}",
                            context.User?.Id, attr.Permission, method.Name);
                    }
                }
            }

            if (missingPermissions.Any())
            {
                _logger.LogWarning("User {UserId} missing required permissions {Permissions} for tool {ToolName}",
                    context.User?.Id, string.Join(", ", missingPermissions), method.Name);

                var errorMessage = missingPermissions.Count == 1
                    ? RbacErrors.Authorization.MissingPermission(missingPermissions[0])
                    : RbacErrors.Authorization.InsufficientPermissions;

                return Result.Fail(errorMessage);
            }

            return Result.Ok();
        }

        private Result CheckRoleRequirements(MethodInfo method, McpAuthorizationContext context)
        {
            var roleAttributes = method.GetCustomAttributes<McpRequireRoleAttribute>().ToList();
            if (!roleAttributes.Any())
            {
                return Result.Ok();
            }

            var missingRoles = new List<string>();

            foreach (var attr in roleAttributes)
            {
                if (!context.HasRole(attr.RoleName))
                {
                    missingRoles.Add(attr.RoleName);
                }
            }

            if (missingRoles.Any())
            {
                _logger.LogWarning("User {UserId} missing required roles {Roles} for tool {ToolName}",
                    context.User?.Id, string.Join(", ", missingRoles), method.Name);

                return Result.Fail(RbacErrors.Authorization.InsufficientPermissions);
            }

            return Result.Ok();
        }

        private Result CheckOrganizationRequirements(MethodInfo method, McpAuthorizationContext context)
        {
            var orgAttribute = method.GetCustomAttribute<McpRequireOrganizationAttribute>();
            if (orgAttribute == null)
            {
                return Result.Ok();
            }

            if (context.Organization == null)
            {
                _logger.LogWarning("User {UserId} has no organization context for tool {ToolName}",
                    context.User?.Id, method.Name);
                return Result.Fail(RbacErrors.Authorization.OrganizationAccessDenied);
            }

            // Check specific organization requirement
            if (!string.IsNullOrEmpty(orgAttribute.OrganizationId) &&
                !context.Organization.Id.Equals(orgAttribute.OrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("User {UserId} not in required organization {RequiredOrgId} for tool {ToolName}",
                    context.User?.Id, orgAttribute.OrganizationId, method.Name);
                return Result.Fail(RbacErrors.Authorization.OrganizationAccessDenied);
            }

            return Result.Ok();
        }
    }
}