namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Request to get user permissions for a specific organization
    /// </summary>
    public record GetUserPermissionsRequest
    {
        /// <summary>
        /// Internal RBAC user ID
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// Organization ID to resolve permissions for
        /// </summary>
        public string OrganizationId { get; init; } = string.Empty;

        /// <summary>
        /// Whether to include permissions inherited from parent organizations
        /// </summary>
        public bool IncludeInherited { get; init; } = true;

        /// <summary>
        /// Whether to include detailed permission metadata
        /// </summary>
        public bool IncludeMetadata { get; init; } = false;

        /// <summary>
        /// Filter permissions by resource type (optional)
        /// </summary>
        public string? ResourceFilter { get; init; }

        /// <summary>
        /// Filter permissions by action type (optional)
        /// </summary>
        public string? ActionFilter { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public GetUserPermissionsRequest() { }

        public GetUserPermissionsRequest(
            string userId,
            string organizationId,
            bool includeInherited = true,
            bool includeMetadata = false,
            string? resourceFilter = null,
            string? actionFilter = null)
        {
            UserId = userId;
            OrganizationId = organizationId;
            IncludeInherited = includeInherited;
            IncludeMetadata = includeMetadata;
            ResourceFilter = resourceFilter;
            ActionFilter = actionFilter;
        }
    }
}