namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Organization membership information for a user
    /// </summary>
    public record UserOrganizationMembership
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
        /// Organization path in hierarchy (e.g., "company.engineering.backend")
        /// </summary>
        public string OrganizationPath { get; init; } = string.Empty;

        /// <summary>
        /// Whether this is the user's primary organization
        /// </summary>
        public bool IsPrimary { get; init; }

        /// <summary>
        /// User's roles in this organization
        /// </summary>
        public List<UserRoleInfo> Roles { get; init; } = new();

        /// <summary>
        /// When the user joined this organization
        /// </summary>
        public DateTime JoinedAt { get; init; }

        /// <summary>
        /// Organization status (Active, Inactive, Suspended)
        /// </summary>
        public string Status { get; init; } = "Active";

        /// <summary>
        /// Organization description
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Parent organization information (if includeHierarchy was true)
        /// </summary>
        public OrganizationHierarchyInfo? Parent { get; init; }

        /// <summary>
        /// Child organizations (if includeHierarchy was true)
        /// </summary>
        public List<OrganizationHierarchyInfo> Children { get; init; } = new();
    }

    /// <summary>
    /// User role information within an organization
    /// </summary>
    public record UserRoleInfo
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
        /// Role description
        /// </summary>
        public string? RoleDescription { get; init; }

        /// <summary>
        /// When the user was assigned this role
        /// </summary>
        public DateTime AssignedAt { get; init; }

        /// <summary>
        /// Whether this role is inheritable to child organizations
        /// </summary>
        public bool IsInheritable { get; init; }
    }

    /// <summary>
    /// Organization hierarchy information
    /// </summary>
    public record OrganizationHierarchyInfo
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
    }

    /// <summary>
    /// Response containing user's organization memberships
    /// </summary>
    public record GetUserOrganizationsResponse
    {
        /// <summary>
        /// User ID that was queried
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// List of organization memberships
        /// </summary>
        public List<UserOrganizationMembership> Organizations { get; init; } = new();

        /// <summary>
        /// Primary organization (if user has one)
        /// </summary>
        public UserOrganizationMembership? PrimaryOrganization { get; init; }

        /// <summary>
        /// Total number of organizations user belongs to
        /// </summary>
        public int TotalCount { get; init; }

        /// <summary>
        /// Whether results were limited
        /// </summary>
        public bool IsLimited { get; init; }

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        public GetUserOrganizationsResponse() { }

        public GetUserOrganizationsResponse(
            string userId,
            List<UserOrganizationMembership> organizations,
            int totalCount,
            bool isLimited = false,
            string requestId = "")
        {
            UserId = userId;
            Organizations = organizations;
            PrimaryOrganization = organizations.FirstOrDefault(o => o.IsPrimary);
            TotalCount = totalCount;
            IsLimited = isLimited;
            RequestId = requestId;
        }
    }
}