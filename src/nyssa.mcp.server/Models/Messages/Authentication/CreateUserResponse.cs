namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Response after creating a new user in the RBAC system
    /// </summary>
    public record CreateUserResponse
    {
        /// <summary>
        /// Internal RBAC user ID (UUID) of the created user
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
        /// When the user was created
        /// </summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>
        /// Initial user status (e.g., "Active", "Pending")
        /// </summary>
        public string Status { get; init; } = "Active";

        /// <summary>
        /// Source system that created this user
        /// </summary>
        public string Source { get; init; } = "WorkOS";

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>
        /// Whether the creation was successful
        /// </summary>
        public bool Success { get; init; } = true;

        /// <summary>
        /// Error message if creation failed
        /// </summary>
        public string? ErrorMessage { get; init; }

        public CreateUserResponse() { }

        public CreateUserResponse(
            string userId,
            string externalUserId,
            string email,
            string firstName,
            string lastName,
            DateTime createdAt,
            string status = "Active",
            string source = "WorkOS",
            string? profilePictureUrl = null,
            string requestId = "",
            bool success = true,
            string? errorMessage = null)
        {
            UserId = userId;
            ExternalUserId = externalUserId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            ProfilePictureUrl = profilePictureUrl;
            CreatedAt = createdAt;
            Status = status;
            Source = source;
            RequestId = requestId;
            Success = success;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Creates a failed user creation response
        /// </summary>
        public static CreateUserResponse Failed(string requestId, string errorMessage)
        {
            return new CreateUserResponse
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}