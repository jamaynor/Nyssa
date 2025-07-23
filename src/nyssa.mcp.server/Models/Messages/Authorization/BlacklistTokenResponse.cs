namespace Nyssa.Mcp.Server.Models.Messages.Authorization
{
    /// <summary>
    /// Response after blacklisting a token
    /// </summary>
    public record BlacklistTokenResponse
    {
        /// <summary>
        /// JWT token identifier that was blacklisted
        /// </summary>
        public string TokenId { get; init; } = string.Empty;

        /// <summary>
        /// Whether the blacklisting was successful
        /// </summary>
        public bool Success { get; init; } = true;

        /// <summary>
        /// When the token was blacklisted
        /// </summary>
        public DateTime BlacklistedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Reason for blacklisting
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// Additional details about the blacklisting
        /// </summary>
        public string? Details { get; init; }

        /// <summary>
        /// User ID associated with the blacklisted token
        /// </summary>
        public string? UserId { get; init; }

        /// <summary>
        /// Organization ID for which the token was issued
        /// </summary>
        public string? OrganizationId { get; init; }

        /// <summary>
        /// ID of the user/system that performed the blacklisting
        /// </summary>
        public string? BlacklistedBy { get; init; }

        /// <summary>
        /// Number of additional tokens that were blacklisted (if batch operation)
        /// </summary>
        public int AdditionalTokensBlacklisted { get; init; } = 0;

        /// <summary>
        /// Error message if blacklisting failed
        /// </summary>
        public string? ErrorMessage { get; init; }

        /// <summary>
        /// Error code if blacklisting failed
        /// </summary>
        public string? ErrorCode { get; init; }

        /// <summary>
        /// Timestamp when the response was generated
        /// </summary>
        public DateTime ResponseAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        public BlacklistTokenResponse() { }

        public BlacklistTokenResponse(
            string tokenId,
            bool success,
            string reason,
            DateTime? blacklistedAt = null,
            string? details = null,
            string? userId = null,
            string? organizationId = null,
            string? blacklistedBy = null,
            int additionalTokensBlacklisted = 0,
            string? errorMessage = null,
            string? errorCode = null,
            string requestId = "")
        {
            TokenId = tokenId;
            Success = success;
            Reason = reason;
            BlacklistedAt = blacklistedAt ?? DateTime.UtcNow;
            Details = details;
            UserId = userId;
            OrganizationId = organizationId;
            BlacklistedBy = blacklistedBy;
            AdditionalTokensBlacklisted = additionalTokensBlacklisted;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
            RequestId = requestId;
        }

        /// <summary>
        /// Creates a successful blacklist response
        /// </summary>
        public static BlacklistTokenResponse CreateSuccess(
            string tokenId,
            string reason,
            string? userId = null,
            string? organizationId = null,
            string? blacklistedBy = null,
            int additionalTokensBlacklisted = 0,
            string requestId = "")
        {
            return new BlacklistTokenResponse(
                tokenId,
                success: true,
                reason: reason,
                userId: userId,
                organizationId: organizationId,
                blacklistedBy: blacklistedBy,
                additionalTokensBlacklisted: additionalTokensBlacklisted,
                requestId: requestId);
        }

        /// <summary>
        /// Creates a failed blacklist response
        /// </summary>
        public static BlacklistTokenResponse CreateFailure(
            string tokenId,
            string errorMessage,
            string? errorCode = null,
            string requestId = "")
        {
            return new BlacklistTokenResponse(
                tokenId,
                success: false,
                reason: "failed",
                errorMessage: errorMessage,
                errorCode: errorCode,
                requestId: requestId);
        }
    }
}