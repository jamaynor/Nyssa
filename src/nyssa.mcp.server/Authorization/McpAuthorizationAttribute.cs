using System;

namespace Nyssa.Mcp.Server.Authorization
{
    /// <summary>
    /// Attribute to specify required permissions for MCP tools
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class McpRequirePermissionAttribute : Attribute
    {
        /// <summary>
        /// Required permission in format "resource:action" (e.g., "users:read", "projects:write")
        /// </summary>
        public string Permission { get; }

        /// <summary>
        /// Optional description of why this permission is needed
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether this permission is required (true) or just recommended (false)
        /// </summary>
        public bool IsRequired { get; set; } = true;

        public McpRequirePermissionAttribute(string permission)
        {
            Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        }

        public McpRequirePermissionAttribute(string resource, string action)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentNullException(nameof(resource));
            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentNullException(nameof(action));

            Permission = $"{resource}:{action}";
        }
    }

    /// <summary>
    /// Attribute to specify required roles for MCP tools
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class McpRequireRoleAttribute : Attribute
    {
        /// <summary>
        /// Required role name
        /// </summary>
        public string RoleName { get; }

        /// <summary>
        /// Whether this role must be in the current organization (true) or any organization (false)
        /// </summary>
        public bool CurrentOrganizationOnly { get; set; } = true;

        /// <summary>
        /// Optional description of why this role is needed
        /// </summary>
        public string? Description { get; set; }

        public McpRequireRoleAttribute(string roleName)
        {
            RoleName = roleName ?? throw new ArgumentNullException(nameof(roleName));
        }
    }

    /// <summary>
    /// Attribute to specify that an MCP tool requires authentication but no specific permissions
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpRequireAuthenticationAttribute : Attribute
    {
        /// <summary>
        /// Optional description
        /// </summary>
        public string? Description { get; set; }

        public McpRequireAuthenticationAttribute() { }

        public McpRequireAuthenticationAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Attribute to specify that an MCP tool allows anonymous access (no authentication required)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpAllowAnonymousAttribute : Attribute
    {
        /// <summary>
        /// Optional description of why anonymous access is allowed
        /// </summary>
        public string? Description { get; set; }

        public McpAllowAnonymousAttribute() { }

        public McpAllowAnonymousAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Attribute to specify organization-level access requirements
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpRequireOrganizationAttribute : Attribute
    {
        /// <summary>
        /// Specific organization ID required (if null, any organization membership is acceptable)
        /// </summary>
        public string? OrganizationId { get; set; }

        /// <summary>
        /// Whether the user must be in their primary organization
        /// </summary>
        public bool PrimaryOrganizationOnly { get; set; } = false;

        /// <summary>
        /// Minimum organization status required
        /// </summary>
        public string? MinimumStatus { get; set; } = "Active";

        /// <summary>
        /// Optional description
        /// </summary>
        public string? Description { get; set; }

        public McpRequireOrganizationAttribute() { }

        public McpRequireOrganizationAttribute(string organizationId)
        {
            OrganizationId = organizationId;
        }
    }

    /// <summary>
    /// Context information extracted from authorization token for MCP tools
    /// </summary>
    public class McpAuthorizationContext
    {
        /// <summary>
        /// Whether the request is authenticated
        /// </summary>
        public bool IsAuthenticated { get; init; }

        /// <summary>
        /// User information from the token
        /// </summary>
        public McpUser? User { get; init; }

        /// <summary>
        /// Organization information from the token
        /// </summary>
        public McpOrganization? Organization { get; init; }

        /// <summary>
        /// List of permissions granted to the user
        /// </summary>
        public List<string> Permissions { get; init; } = new();

        /// <summary>
        /// List of roles granted to the user
        /// </summary>
        public List<McpRole> Roles { get; init; } = new();

        /// <summary>
        /// Original JWT token
        /// </summary>
        public string? Token { get; init; }

        /// <summary>
        /// Token expiration time
        /// </summary>
        public DateTime? ExpiresAt { get; init; }

        /// <summary>
        /// Whether the token includes inherited permissions
        /// </summary>
        public bool IncludesInherited { get; init; }

        /// <summary>
        /// Additional metadata from the token
        /// </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();

        /// <summary>
        /// Checks if the user has a specific permission
        /// </summary>
        public bool HasPermission(string permission)
        {
            return Permissions.Contains(permission);
        }

        /// <summary>
        /// Checks if the user has permission for a resource and action
        /// </summary>
        public bool HasPermission(string resource, string action)
        {
            return HasPermission($"{resource}:{action}");
        }

        /// <summary>
        /// Checks if the user has any of the specified permissions
        /// </summary>
        public bool HasAnyPermission(params string[] permissions)
        {
            return permissions.Any(p => Permissions.Contains(p));
        }

        /// <summary>
        /// Checks if the user has all of the specified permissions
        /// </summary>
        public bool HasAllPermissions(params string[] permissions)
        {
            return permissions.All(p => Permissions.Contains(p));
        }

        /// <summary>
        /// Checks if the user has a specific role
        /// </summary>
        public bool HasRole(string roleName)
        {
            return Roles.Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets permissions for a specific resource
        /// </summary>
        public List<string> GetResourcePermissions(string resource)
        {
            return Permissions.Where(p => p.StartsWith($"{resource}:")).ToList();
        }
    }

    /// <summary>
    /// User information for MCP authorization context
    /// </summary>
    public record McpUser
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string ExternalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Organization information for MCP authorization context
    /// </summary>
    public record McpOrganization
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    /// <summary>
    /// Role information for MCP authorization context
    /// </summary>
    public record McpRole
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsInheritable { get; init; }
    }
}