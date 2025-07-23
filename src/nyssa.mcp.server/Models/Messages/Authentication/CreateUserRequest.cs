namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Request to create a new user in the RBAC system
    /// </summary>
    public record CreateUserRequest
    {
        /// <summary>
        /// External WorkOS user ID
        /// </summary>
        public string ExternalUserId { get; init; } = string.Empty;

        /// <summary>
        /// User email address (must be unique)
        /// </summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>
        /// User first name
        /// </summary>
        public string FirstName { get; init; } = string.Empty;

        /// <summary>
        /// User last name
        /// </summary>
        public string LastName { get; init; } = string.Empty;

        /// <summary>
        /// User profile picture URL (optional)
        /// </summary>
        public string? ProfilePictureUrl { get; init; }

        /// <summary>
        /// Source system that created this user (e.g., "WorkOS", "Manual")
        /// </summary>
        public string Source { get; init; } = "WorkOS";

        /// <summary>
        /// Additional metadata about the user (JSON format)
        /// </summary>
        public string? Metadata { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        /// <summary>
        /// ID of the requesting user/system for audit purposes
        /// </summary>
        public string? RequestedBy { get; init; }

        public CreateUserRequest() { }

        public CreateUserRequest(
            string externalUserId,
            string email,
            string firstName,
            string lastName,
            string? profilePictureUrl = null,
            string source = "WorkOS",
            string? metadata = null,
            string? requestedBy = null)
        {
            ExternalUserId = externalUserId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            ProfilePictureUrl = profilePictureUrl;
            Source = source;
            Metadata = metadata;
            RequestedBy = requestedBy;
        }
    }
}