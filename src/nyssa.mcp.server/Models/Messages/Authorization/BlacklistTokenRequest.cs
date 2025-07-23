namespace Nyssa.Mcp.Server.Models.Messages.Authorization
{
    /// <summary>
    /// Token blacklist reasons
    /// </summary>
    public static class BlacklistReasons
    {
        public const string UserLogout = "user_logout";
        public const string UserDeactivated = "user_deactivated";
        public const string PasswordChanged = "password_changed";
        public const string SecurityBreach = "security_breach";
        public const string TokenCompromised = "token_compromised";
        public const string AdminRevoked = "admin_revoked";
        public const string OrganizationRemoved = "organization_removed";
        public const string RoleRevoked = "role_revoked";
        public const string PolicyViolation = "policy_violation";
        public const string Expired = "expired";
        public const string ManualRevocation = "manual_revocation";
    }

    /// <summary>
    /// Request to blacklist/revoke a JWT token
    /// </summary>
    public record BlacklistTokenRequest
    {
        /// <summary>
        /// JWT token identifier (jti claim)
        /// </summary>
        public string TokenId { get; init; } = string.Empty;

        /// <summary>
        /// Full JWT token (for validation and metadata extraction)
        /// </summary>
        public string? Token { get; init; }

        /// <summary>
        /// Reason for blacklisting (see BlacklistReasons constants)
        /// </summary>
        public string Reason { get; init; } = BlacklistReasons.ManualRevocation;

        /// <summary>
        /// Additional details about the blacklisting
        /// </summary>
        public string? Details { get; init; }

        /// <summary>
        /// User ID associated with the token
        /// </summary>
        public string? UserId { get; init; }

        /// <summary>
        /// Organization ID for which the token was issued
        /// </summary>
        public string? OrganizationId { get; init; }

        /// <summary>
        /// ID of the user/system requesting the blacklisting
        /// </summary>
        public string? RequestedBy { get; init; }

        /// <summary>
        /// Source of the blacklist request (e.g., "user_logout", "admin_panel")
        /// </summary>
        public string Source { get; init; } = "mcp_task";

        /// <summary>
        /// IP address of the requesting client (if available)
        /// </summary>
        public string? IpAddress { get; init; }

        /// <summary>
        /// Whether to blacklist all tokens for the user (not just this one)
        /// </summary>
        public bool BlacklistAllUserTokens { get; init; } = false;

        /// <summary>
        /// Whether to blacklist all tokens for the user in this organization
        /// </summary>
        public bool BlacklistUserOrgTokens { get; init; } = false;

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public BlacklistTokenRequest() { }

        public BlacklistTokenRequest(
            string tokenId,
            string reason,
            string? token = null,
            string? details = null,
            string? userId = null,
            string? organizationId = null,
            string? requestedBy = null,
            string source = "mcp_task",
            string? ipAddress = null,
            bool blacklistAllUserTokens = false,
            bool blacklistUserOrgTokens = false)
        {
            TokenId = tokenId;
            Reason = reason;
            Token = token;
            Details = details;
            UserId = userId;
            OrganizationId = organizationId;
            RequestedBy = requestedBy;
            Source = source;
            IpAddress = ipAddress;
            BlacklistAllUserTokens = blacklistAllUserTokens;
            BlacklistUserOrgTokens = blacklistUserOrgTokens;
        }

        /// <summary>
        /// Creates a blacklist request for user logout
        /// </summary>
        public static BlacklistTokenRequest ForUserLogout(
            string tokenId,
            string userId,
            string? organizationId = null,
            string? ipAddress = null,
            bool blacklistAllTokens = true)
        {
            return new BlacklistTokenRequest(
                tokenId,
                BlacklistReasons.UserLogout,
                userId: userId,
                organizationId: organizationId,
                source: "user_logout",
                ipAddress: ipAddress,
                blacklistAllUserTokens: blacklistAllTokens);
        }

        /// <summary>
        /// Creates a blacklist request for security breach
        /// </summary>
        public static BlacklistTokenRequest ForSecurityBreach(
            string tokenId,
            string details,
            string? userId = null,
            string? requestedBy = null,
            bool blacklistAllUserTokens = true)
        {
            return new BlacklistTokenRequest(
                tokenId,
                BlacklistReasons.SecurityBreach,
                details: details,
                userId: userId,
                requestedBy: requestedBy,
                source: "security_system",
                blacklistAllUserTokens: blacklistAllUserTokens);
        }

        /// <summary>
        /// Creates a blacklist request for admin revocation
        /// </summary>
        public static BlacklistTokenRequest ForAdminRevocation(
            string tokenId,
            string adminUserId,
            string reason,
            string? userId = null,
            string? organizationId = null)
        {
            return new BlacklistTokenRequest(
                tokenId,
                BlacklistReasons.AdminRevoked,
                details: reason,
                userId: userId,
                organizationId: organizationId,
                requestedBy: adminUserId,
                source: "admin_panel");
        }
    }
}