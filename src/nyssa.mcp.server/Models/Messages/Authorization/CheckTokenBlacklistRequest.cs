namespace Nyssa.Mcp.Server.Models.Messages.Authorization
{
    /// <summary>
    /// Request to check if a JWT token is blacklisted/revoked
    /// </summary>
    public record CheckTokenBlacklistRequest
    {
        /// <summary>
        /// JWT token identifier (jti claim)
        /// </summary>
        public string TokenId { get; init; } = string.Empty;

        /// <summary>
        /// Full JWT token (for additional validation if needed)
        /// </summary>
        public string? Token { get; init; }

        /// <summary>
        /// User ID associated with the token (for optimization)
        /// </summary>
        public string? UserId { get; init; }

        /// <summary>
        /// Organization ID for which the token was issued
        /// </summary>
        public string? OrganizationId { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Source of the blacklist check (e.g., "mcp_task", "api_endpoint")
        /// </summary>
        public string Source { get; init; } = "mcp_task";

        public CheckTokenBlacklistRequest() { }

        public CheckTokenBlacklistRequest(
            string tokenId,
            string? token = null,
            string? userId = null,
            string? organizationId = null,
            string source = "mcp_task")
        {
            TokenId = tokenId;
            Token = token;
            UserId = userId;
            OrganizationId = organizationId;
            Source = source;
        }
    }
}