namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Request to get all organizations a user belongs to
    /// </summary>
    public record GetUserOrganizationsRequest
    {
        /// <summary>
        /// Internal RBAC user ID
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// Whether to include inactive organizations
        /// </summary>
        public bool IncludeInactive { get; init; } = false;

        /// <summary>
        /// Whether to include organization hierarchy information
        /// </summary>
        public bool IncludeHierarchy { get; init; } = true;

        /// <summary>
        /// Whether to include user roles within each organization
        /// </summary>
        public bool IncludeRoles { get; init; } = false;

        /// <summary>
        /// Maximum number of organizations to return (0 = no limit)
        /// </summary>
        public int Limit { get; init; } = 0;

        /// <summary>
        /// Filter organizations by status (e.g., "Active", "Inactive", "Suspended")
        /// </summary>
        public string? StatusFilter { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public GetUserOrganizationsRequest() { }

        public GetUserOrganizationsRequest(
            string userId,
            bool includeInactive = false,
            bool includeHierarchy = true,
            bool includeRoles = false,
            int limit = 0)
        {
            UserId = userId;
            IncludeInactive = includeInactive;
            IncludeHierarchy = includeHierarchy;
            IncludeRoles = includeRoles;
            Limit = limit;
        }
    }
}