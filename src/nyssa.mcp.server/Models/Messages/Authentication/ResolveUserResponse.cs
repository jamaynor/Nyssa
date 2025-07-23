namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Response containing resolved user information from RBAC system
    /// </summary>
    public record ResolveUserResponse
    {
        /// <summary>
        /// Internal RBAC user ID (UUID)
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// External WorkOS user ID
        /// </summary>
        public string ExternalUserId { get; init; } = string.Empty;

        /// <summary>
        /// User email address
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
        /// When the user was created in the RBAC system
        /// </summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>
        /// When the user was last updated
        /// </summary>
        public DateTime UpdatedAt { get; init; }

        /// <summary>
        /// Whether this user was newly created during resolution
        /// </summary>
        public bool IsNewUser { get; init; }

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        public ResolveUserResponse() { }

        public ResolveUserResponse(
            string userId,
            string externalUserId,
            string email,
            string firstName,
            string lastName,
            DateTime createdAt,
            DateTime updatedAt,
            bool isNewUser = false,
            string? profilePictureUrl = null,
            string requestId = "")
        {
            UserId = userId;
            ExternalUserId = externalUserId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            ProfilePictureUrl = profilePictureUrl;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            IsNewUser = isNewUser;
            RequestId = requestId;
        }
    }
}