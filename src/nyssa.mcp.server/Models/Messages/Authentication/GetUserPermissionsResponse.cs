namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Permission information for a user
    /// </summary>
    public record UserPermission
    {
        /// <summary>
        /// Permission ID
        /// </summary>
        public string PermissionId { get; init; } = string.Empty;

        /// <summary>
        /// Permission code (e.g., "users:read", "projects:write")
        /// </summary>
        public string Permission { get; init; } = string.Empty;

        /// <summary>
        /// Resource type (e.g., "users", "projects", "organizations")
        /// </summary>
        public string Resource { get; init; } = string.Empty;

        /// <summary>
        /// Action type (e.g., "read", "write", "delete", "admin")
        /// </summary>
        public string Action { get; init; } = string.Empty;

        /// <summary>
        /// Permission description
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Role that granted this permission
        /// </summary>
        public PermissionSourceRole? SourceRole { get; init; }

        /// <summary>
        /// Organization that granted this permission
        /// </summary>
        public PermissionSourceOrganization? SourceOrganization { get; init; }

        /// <summary>
        /// Whether this permission was inherited from a parent organization
        /// </summary>
        public bool IsInherited { get; init; }

        /// <summary>
        /// Permission constraints or metadata (JSON format)
        /// </summary>
        public string? Metadata { get; init; }

        /// <summary>
        /// When this permission was granted
        /// </summary>
        public DateTime GrantedAt { get; init; }
    }

    /// <summary>
    /// Role that granted a permission
    /// </summary>
    public record PermissionSourceRole
    {
        /// <summary>
        /// Role ID
        /// </summary>
        public string RoleId { get; init; } = string.Empty;

        /// <summary>
        /// Role name
        /// </summary>
        public string RoleName { get; init; } = string.Empty;

        /// <summary>
        /// Organization where the role was assigned
        /// </summary>
        public string OrganizationId { get; init; } = string.Empty;

        /// <summary>
        /// Organization name
        /// </summary>
        public string OrganizationName { get; init; } = string.Empty;
    }

    /// <summary>
    /// Organization that granted a permission
    /// </summary>
    public record PermissionSourceOrganization
    {
        /// <summary>
        /// Organization ID
        /// </summary>
        public string OrganizationId { get; init; } = string.Empty;

        /// <summary>
        /// Organization name
        /// </summary>
        public string OrganizationName { get; init; } = string.Empty;

        /// <summary>
        /// Organization path
        /// </summary>
        public string OrganizationPath { get; init; } = string.Empty;

        /// <summary>
        /// Whether this is the organization requested or a parent
        /// </summary>
        public bool IsParent { get; init; }
    }

    /// <summary>
    /// Response containing user permissions for an organization
    /// </summary>
    public record GetUserPermissionsResponse
    {
        /// <summary>
        /// User ID that was queried
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// Organization ID that was queried
        /// </summary>
        public string OrganizationId { get; init; } = string.Empty;

        /// <summary>
        /// Organization name
        /// </summary>
        public string OrganizationName { get; init; } = string.Empty;

        /// <summary>
        /// List of user permissions
        /// </summary>
        public List<UserPermission> Permissions { get; init; } = new();

        /// <summary>
        /// Simple list of permission codes for quick checking
        /// </summary>
        public List<string> PermissionCodes { get; init; } = new();

        /// <summary>
        /// User roles in this organization that granted permissions
        /// </summary>
        public List<UserRoleInfo> Roles { get; init; } = new();

        /// <summary>
        /// Whether inherited permissions were included
        /// </summary>
        public bool IncludedInherited { get; init; }

        /// <summary>
        /// Number of inherited permissions
        /// </summary>
        public int InheritedPermissionCount { get; init; }

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        public GetUserPermissionsResponse() { }

        public GetUserPermissionsResponse(
            string userId,
            string organizationId,
            string organizationName,
            List<UserPermission> permissions,
            List<UserRoleInfo> roles,
            bool includedInherited = true,
            string requestId = "")
        {
            UserId = userId;
            OrganizationId = organizationId;
            OrganizationName = organizationName;
            Permissions = permissions;
            PermissionCodes = permissions.Select(p => p.Permission).ToList();
            Roles = roles;
            IncludedInherited = includedInherited;
            InheritedPermissionCount = permissions.Count(p => p.IsInherited);
            RequestId = requestId;
        }

        /// <summary>
        /// Checks if the user has a specific permission
        /// </summary>
        public bool HasPermission(string permission)
        {
            return PermissionCodes.Contains(permission);
        }

        /// <summary>
        /// Checks if the user has permissions for a specific resource and action
        /// </summary>
        public bool HasPermission(string resource, string action)
        {
            var permission = $"{resource}:{action}";
            return HasPermission(permission);
        }
    }
}