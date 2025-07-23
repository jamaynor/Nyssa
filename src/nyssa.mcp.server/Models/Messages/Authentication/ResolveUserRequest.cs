namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Request to resolve a user by their external WorkOS ID
    /// </summary>
    public record ResolveUserRequest
    {
        /// <summary>
        /// WorkOS user ID from the authentication token
        /// </summary>
        public string WorkOSUserId { get; init; } = string.Empty;

        /// <summary>
        /// WorkOS user email for fallback user creation
        /// </summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>
        /// WorkOS user first name for user creation
        /// </summary>
        public string FirstName { get; init; } = string.Empty;

        /// <summary>
        /// WorkOS user last name for user creation
        /// </summary>
        public string LastName { get; init; } = string.Empty;

        /// <summary>
        /// WorkOS user profile picture URL (optional)
        /// </summary>
        public string? ProfilePictureUrl { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public ResolveUserRequest() { }

        public ResolveUserRequest(
            string workOSUserId, 
            string email, 
            string firstName, 
            string lastName, 
            string? profilePictureUrl = null)
        {
            WorkOSUserId = workOSUserId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            ProfilePictureUrl = profilePictureUrl;
        }
    }
}