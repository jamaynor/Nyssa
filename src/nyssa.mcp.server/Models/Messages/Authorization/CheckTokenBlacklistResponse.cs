namespace Nyssa.Mcp.Server.Models.Messages.Authorization
{
    /// <summary>
    /// Response indicating whether a token is blacklisted
    /// </summary>
    public record CheckTokenBlacklistResponse
    {
        /// <summary>
        /// JWT token identifier that was checked
        /// </summary>
        public string TokenId { get; init; } = string.Empty;

        /// <summary>
        /// Whether the token is blacklisted/revoked
        /// </summary>
        public bool IsBlacklisted { get; init; }

        /// <summary>
        /// When the token was blacklisted (if applicable)
        /// </summary>
        public DateTime? BlacklistedAt { get; init; }

        /// <summary>
        /// Reason for blacklisting (if applicable)
        /// </summary>
        public string? BlacklistReason { get; init; }

        /// <summary>
        /// ID of the user/system that blacklisted the token
        /// </summary>
        public string? BlacklistedBy { get; init; }

        /// <summary>
        /// Original token expiration time
        /// </summary>
        public DateTime? TokenExpiresAt { get; init; }

        /// <summary>
        /// User ID associated with the token
        /// </summary>
        public string? UserId { get; init; }

        /// <summary>
        /// Organization ID for which the token was issued
        /// </summary>
        public string? OrganizationId { get; init; }

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        public CheckTokenBlacklistResponse() { }

        public CheckTokenBlacklistResponse(
            string tokenId,
            bool isBlacklisted,
            DateTime? blacklistedAt = null,
            string? blacklistReason = null,
            string? blacklistedBy = null,
            DateTime? tokenExpiresAt = null,
            string? userId = null,
            string? organizationId = null,
            string requestId = "")
        {
            TokenId = tokenId;
            IsBlacklisted = isBlacklisted;
            BlacklistedAt = blacklistedAt;
            BlacklistReason = blacklistReason;
            BlacklistedBy = blacklistedBy;
            TokenExpiresAt = tokenExpiresAt;
            UserId = userId;
            OrganizationId = organizationId;
            RequestId = requestId;
        }

        /// <summary>
        /// Creates response for a non-blacklisted token
        /// </summary>
        public static CheckTokenBlacklistResponse NotBlacklisted(
            string tokenId,
            string requestId = "",
            string? userId = null,
            string? organizationId = null)
        {
            return new CheckTokenBlacklistResponse(
                tokenId, 
                isBlacklisted: false, 
                requestId: requestId,
                userId: userId,
                organizationId: organizationId);
        }

        /// <summary>
        /// Creates response for a blacklisted token
        /// </summary>
        public static CheckTokenBlacklistResponse Blacklisted(
            string tokenId,
            DateTime blacklistedAt,
            string blacklistReason,
            string? blacklistedBy = null,
            string requestId = "",
            string? userId = null,
            string? organizationId = null)
        {
            return new CheckTokenBlacklistResponse(
                tokenId,
                isBlacklisted: true,
                blacklistedAt: blacklistedAt,
                blacklistReason: blacklistReason,
                blacklistedBy: blacklistedBy,
                requestId: requestId,
                userId: userId,
                organizationId: organizationId);
        }
    }
}