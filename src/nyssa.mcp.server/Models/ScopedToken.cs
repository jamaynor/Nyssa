using Nyssa.Mcp.Server.Models.Messages.Authentication;

namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Represents the JWT payload structure for scoped tokens with embedded permissions
    /// </summary>
    public record ScopedTokenPayload
    {
        /// <summary>
        /// JWT issuer
        /// </summary>
        public string Iss { get; init; } = "nyssa-mcp-server";

        /// <summary>
        /// JWT subject (WorkOS user ID)
        /// </summary>
        public string Sub { get; init; } = string.Empty;

        /// <summary>
        /// JWT audience
        /// </summary>
        public string Aud { get; init; } = "nyssa-api";

        /// <summary>
        /// JWT expiration time (Unix timestamp)
        /// </summary>
        public long Exp { get; init; }

        /// <summary>
        /// JWT issued at time (Unix timestamp)
        /// </summary>
        public long Iat { get; init; }

        /// <summary>
        /// JWT ID (unique identifier for this token)
        /// </summary>
        public string Jti { get; init; } = string.Empty;

        /// <summary>
        /// User information embedded in the token
        /// </summary>
        public ScopedTokenUser User { get; init; } = new();

        /// <summary>
        /// Organization information for this token's scope
        /// </summary>
        public ScopedTokenOrganization Organization { get; init; } = new();

        /// <summary>
        /// List of permission codes granted to the user
        /// </summary>
        public List<string> Permissions { get; init; } = new();

        /// <summary>
        /// User roles that granted the permissions
        /// </summary>
        public List<ScopedTokenRole> Roles { get; init; } = new();

        /// <summary>
        /// Token type identifier
        /// </summary>
        public string TokenType { get; init; } = "scoped";

        /// <summary>
        /// Token scope (organization-specific)
        /// </summary>
        public string Scope { get; init; } = string.Empty;

        /// <summary>
        /// Whether this token includes inherited permissions
        /// </summary>
        public bool IncludesInherited { get; init; } = true;

        /// <summary>
        /// Token generation metadata
        /// </summary>
        public ScopedTokenMetadata Metadata { get; init; } = new();
    }

    /// <summary>
    /// User information embedded in scoped token
    /// </summary>
    public record ScopedTokenUser
    {
        /// <summary>
        /// Internal RBAC user ID
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// User email address
        /// </summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>
        /// User's full name
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// User's first name
        /// </summary>
        public string FirstName { get; init; } = string.Empty;

        /// <summary>
        /// User's last name
        /// </summary>
        public string LastName { get; init; } = string.Empty;

        /// <summary>
        /// External WorkOS user ID
        /// </summary>
        public string ExternalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Organization information embedded in scoped token
    /// </summary>
    public record ScopedTokenOrganization
    {
        /// <summary>
        /// Organization ID
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Organization name
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Organization path in hierarchy
        /// </summary>
        public string Path { get; init; } = string.Empty;
    }

    /// <summary>
    /// Role information embedded in scoped token
    /// </summary>
    public record ScopedTokenRole
    {
        /// <summary>
        /// Role ID
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Role name
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Whether this role is inheritable
        /// </summary>
        public bool IsInheritable { get; init; }
    }

    /// <summary>
    /// Token generation metadata
    /// </summary>
    public record ScopedTokenMetadata
    {
        /// <summary>
        /// Token generation timestamp
        /// </summary>
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Source of token generation (e.g., "workos_auth", "token_refresh")
        /// </summary>
        public string Source { get; init; } = "workos_auth";

        /// <summary>
        /// IP address of the requesting client
        /// </summary>
        public string? IpAddress { get; init; }

        /// <summary>
        /// User agent of the requesting client
        /// </summary>
        public string? UserAgent { get; init; }

        /// <summary>
        /// Session ID associated with this token
        /// </summary>
        public string? SessionId { get; init; }

        /// <summary>
        /// Number of permissions included in this token
        /// </summary>
        public int PermissionCount { get; init; }

        /// <summary>
        /// Number of inherited permissions
        /// </summary>
        public int InheritedPermissionCount { get; init; }
    }

    /// <summary>
    /// Represents a generated scoped JWT token with metadata
    /// </summary>
    public record ScopedToken
    {
        /// <summary>
        /// The JWT token string
        /// </summary>
        public string Token { get; init; } = string.Empty;

        /// <summary>
        /// Token expiration time
        /// </summary>
        public DateTime ExpiresAt { get; init; }

        /// <summary>
        /// The decoded payload (for convenience, not sent to client)
        /// </summary>
        public ScopedTokenPayload Payload { get; init; } = new();

        /// <summary>
        /// Token generation metadata
        /// </summary>
        public ScopedTokenMetadata Metadata { get; init; } = new();

        public ScopedToken() { }

        public ScopedToken(string token, DateTime expiresAt, ScopedTokenPayload payload, ScopedTokenMetadata metadata)
        {
            Token = token;
            ExpiresAt = expiresAt;
            Payload = payload;
            Metadata = metadata;
        }

        /// <summary>
        /// Static factory method for creating a scoped token from RBAC data
        /// </summary>
        public static ScopedToken Create(
            string token,
            string jti,
            DateTime expiresAt,
            string workosUserId,
            RbacUser user,
            UserOrganizationMembership organization,
            GetUserPermissionsResponse permissions,
            string? ipAddress = null,
            string? userAgent = null,
            string? sessionId = null)
        {
            var payload = new ScopedTokenPayload
            {
                Sub = workosUserId,
                Jti = jti,
                Exp = ((DateTimeOffset)expiresAt).ToUnixTimeSeconds(),
                Iat = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                User = new ScopedTokenUser
                {
                    Id = user.Id,
                    Email = user.Email,
                    Name = $"{user.FirstName} {user.LastName}".Trim(),
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    ExternalId = user.ExternalId
                },
                Organization = new ScopedTokenOrganization
                {
                    Id = organization.OrganizationId,
                    Name = organization.OrganizationName,
                    Path = organization.OrganizationPath
                },
                Permissions = permissions.PermissionCodes,
                Roles = permissions.Roles.Select(r => new ScopedTokenRole
                {
                    Id = r.RoleId,
                    Name = r.RoleName,
                    IsInheritable = r.IsInheritable
                }).ToList(),
                Scope = $"org:{organization.OrganizationId}",
                IncludesInherited = permissions.IncludedInherited
            };

            var metadata = new ScopedTokenMetadata
            {
                IpAddress = ipAddress,
                UserAgent = userAgent,
                SessionId = sessionId,
                PermissionCount = permissions.Permissions.Count,
                InheritedPermissionCount = permissions.InheritedPermissionCount
            };

            payload = payload with { Metadata = metadata };

            return new ScopedToken(token, expiresAt, payload, metadata);
        }
    }

    /// <summary>
    /// RBAC user model (should match the database structure)
    /// </summary>
    public record RbacUser
    {
        /// <summary>
        /// Internal user ID (UUID)
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// External user ID from WorkOS
        /// </summary>
        public string ExternalId { get; init; } = string.Empty;

        /// <summary>
        /// User email address
        /// </summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>
        /// User's first name
        /// </summary>
        public string FirstName { get; init; } = string.Empty;

        /// <summary>
        /// User's last name
        /// </summary>
        public string LastName { get; init; } = string.Empty;

        /// <summary>
        /// When the user was created
        /// </summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>
        /// When the user was last updated
        /// </summary>
        public DateTime UpdatedAt { get; init; }

        /// <summary>
        /// User status (Active, Inactive, Suspended)
        /// </summary>
        public string Status { get; init; } = "Active";

        /// <summary>
        /// Source system that created this user
        /// </summary>
        public string Source { get; init; } = "WorkOS";
    }
}