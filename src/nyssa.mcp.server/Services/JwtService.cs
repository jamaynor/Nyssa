using Microsoft.IdentityModel.Tokens;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Services
{
    /// <summary>
    /// JWT configuration options
    /// </summary>
    public record JwtOptions
    {
        /// <summary>
        /// Configuration section name
        /// </summary>
        public static readonly string SectionName = "Jwt";

        /// <summary>
        /// JWT secret key for signing tokens
        /// </summary>
        public string SecretKey { get; init; } = string.Empty;

        /// <summary>
        /// JWT issuer
        /// </summary>
        public string Issuer { get; init; } = "nyssa-mcp-server";

        /// <summary>
        /// JWT audience
        /// </summary>
        public string Audience { get; init; } = "nyssa-api";

        /// <summary>
        /// Token expiration time in minutes
        /// </summary>
        public int ExpirationMinutes { get; init; } = 60;

        /// <summary>
        /// Whether to include detailed metadata in tokens
        /// </summary>
        public bool IncludeMetadata { get; init; } = true;

        /// <summary>
        /// Maximum number of permissions to embed in a token
        /// </summary>
        public int MaxPermissions { get; init; } = 500;

        /// <summary>
        /// Algorithm used for signing (default: HS256)
        /// </summary>
        public string Algorithm { get; init; } = SecurityAlgorithms.HmacSha256;
    }

    /// <summary>
    /// Service for generating and validating scoped JWT tokens with embedded permissions
    /// </summary>
    public interface IJwtService
    {
        /// <summary>
        /// Generates a scoped JWT token with embedded permissions
        /// </summary>
        Task<Result<ScopedToken>> GenerateScopedTokenAsync(
            string workosUserId,
            RbacUser user,
            UserOrganizationMembership organization,
            GetUserPermissionsResponse permissions,
            string? ipAddress = null,
            string? userAgent = null,
            string? sessionId = null);

        /// <summary>
        /// Validates a JWT token and extracts its payload
        /// </summary>
        Result<ScopedTokenPayload> ValidateToken(string token);

        /// <summary>
        /// Extracts the JTI (JWT ID) from a token without full validation
        /// </summary>
        Result<string> ExtractJti(string token);

        /// <summary>
        /// Checks if a token has a specific permission
        /// </summary>
        Result<bool> HasPermission(string token, string permission);

        /// <summary>
        /// Checks if a token has permission for a resource and action
        /// </summary>
        Result<bool> HasPermission(string token, string resource, string action);

        /// <summary>
        /// Gets the user ID from a token
        /// </summary>
        Result<string> GetUserId(string token);

        /// <summary>
        /// Gets the organization ID from a token
        /// </summary>
        Result<string> GetOrganizationId(string token);
    }

    /// <summary>
    /// Implementation of JWT service for scoped tokens
    /// </summary>
    public class JwtService : IJwtService
    {
        private readonly JwtOptions _options;
        private readonly ILogger<JwtService> _logger;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly TokenValidationParameters _validationParameters;

        public JwtService(JwtOptions options, ILogger<JwtService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new ArgumentException("JWT SecretKey cannot be null or empty", nameof(options));
            }

            _tokenHandler = new JwtSecurityTokenHandler();
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));

            // Clear default claim type mappings to preserve custom claims
            _tokenHandler.InboundClaimTypeMap.Clear();
            
            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateLifetime = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                NameClaimType = "user_name", // Don't map to standard name claim
                RoleClaimType = "roles" // Don't map to standard role claim
            };
        }

        public Task<Result<ScopedToken>> GenerateScopedTokenAsync(
            string workosUserId,
            RbacUser user,
            UserOrganizationMembership organization,
            GetUserPermissionsResponse permissions,
            string? ipAddress = null,
            string? userAgent = null,
            string? sessionId = null)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(workosUserId))
                    return Task.FromResult(Result<ScopedToken>.Fail(RbacErrors.Authentication.InvalidToken));

                if (permissions.Permissions.Count > _options.MaxPermissions)
                {
                    _logger.LogWarning("User {UserId} has {PermissionCount} permissions, which exceeds the maximum of {MaxPermissions}",
                        user.Id, permissions.Permissions.Count, _options.MaxPermissions);
                    
                    return Task.FromResult(Result<ScopedToken>.Fail(RbacErrors.Authorization.InsufficientPermissions));
                }

                // Generate unique token ID
                var jti = GenerateJti();
                var now = DateTime.UtcNow;
                var expiresAt = now.AddMinutes(_options.ExpirationMinutes);

                // Create claims for the JWT
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, workosUserId),
                    new(JwtRegisteredClaimNames.Jti, jti),
                    new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new("user_id", user.Id),
                    new("user_email", user.Email),
                    new("user_name", $"{user.FirstName} {user.LastName}".Trim()),
                    new("org_id", organization.OrganizationId),
                    new("org_name", organization.OrganizationName),
                    new("org_path", organization.OrganizationPath),
                    new("token_type", "scoped"),
                    new("scope", $"org:{organization.OrganizationId}"),
                    new("includes_inherited", permissions.IncludedInherited.ToString().ToLower())
                };

                // Add permissions as claims
                foreach (var permission in permissions.PermissionCodes)
                {
                    claims.Add(new Claim("permissions", permission));
                }

                // Add roles as claims
                foreach (var role in permissions.Roles)
                {
                    claims.Add(new Claim("roles", JsonSerializer.Serialize(new
                    {
                        id = role.RoleId,
                        name = role.RoleName,
                        is_inheritable = role.IsInheritable
                    })));
                }

                // Add metadata if enabled
                if (_options.IncludeMetadata)
                {
                    claims.Add(new Claim("generated_at", now.ToString("O")));
                    claims.Add(new Claim("source", "workos_auth"));
                    claims.Add(new Claim("permission_count", permissions.Permissions.Count.ToString()));
                    claims.Add(new Claim("inherited_count", permissions.InheritedPermissionCount.ToString()));

                    if (!string.IsNullOrWhiteSpace(ipAddress))
                        claims.Add(new Claim("ip_address", ipAddress));
                    if (!string.IsNullOrWhiteSpace(userAgent))
                        claims.Add(new Claim("user_agent", userAgent));
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        claims.Add(new Claim("session_id", sessionId));
                }

                // Create token descriptor
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = expiresAt,
                    Issuer = _options.Issuer,
                    Audience = _options.Audience,
                    SigningCredentials = new SigningCredentials(_signingKey, _options.Algorithm)
                };

                // Generate the token
                var securityToken = _tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = _tokenHandler.WriteToken(securityToken);

                // Create the scoped token response
                var scopedToken = ScopedToken.Create(
                    tokenString,
                    jti,
                    expiresAt,
                    workosUserId,
                    user,
                    organization,
                    permissions,
                    ipAddress,
                    userAgent,
                    sessionId);

                _logger.LogInformation("Generated scoped token for user {UserId} in organization {OrgId} with {PermissionCount} permissions",
                    user.Id, organization.OrganizationId, permissions.Permissions.Count);

                return Task.FromResult(Result<ScopedToken>.Ok(scopedToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate scoped token for user {UserId}", user.Id);
                return Task.FromResult(Result<ScopedToken>.Fail(RbacErrors.ExternalService.JwtSigningError));
            }
        }

        public Result<ScopedTokenPayload> ValidateToken(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return Result<ScopedTokenPayload>.Fail(RbacErrors.Authentication.InvalidToken);

                // Validate the token
                var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

                if (validatedToken is not JwtSecurityToken jwtToken)
                    return Result<ScopedTokenPayload>.Fail(RbacErrors.Authentication.InvalidToken);

                // Extract payload from claims
                var payload = ExtractPayloadFromClaims(principal.Claims);
                
                _logger.LogDebug("Successfully validated token {Jti} for user {UserId}", 
                    payload.Jti, payload.User.Id);

                return Result<ScopedTokenPayload>.Ok(payload);
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogWarning("Token validation failed: token expired");
                return Result<ScopedTokenPayload>.Fail(RbacErrors.Authentication.TokenExpired);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed: {Message}", ex.Message);
                return Result<ScopedTokenPayload>.Fail(RbacErrors.ExternalService.JwtValidationError);
            }
        }

        public Result<string> ExtractJti(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return Result<string>.Fail(RbacErrors.Authentication.InvalidToken);

                var jwtToken = _tokenHandler.ReadJwtToken(token);
                var jti = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

                if (string.IsNullOrWhiteSpace(jti))
                    return Result<string>.Fail(RbacErrors.Authentication.InvalidToken);

                return Result<string>.Ok(jti);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract JTI from token: {Message}", ex.Message);
                return Result<string>.Fail(RbacErrors.Authentication.InvalidToken);
            }
        }

        public Result<bool> HasPermission(string token, string permission)
        {
            var validationResult = ValidateToken(token);
            if (!validationResult.Success)
                return Result<bool>.Fail(validationResult.Errors);

            var hasPermission = validationResult.Value.Permissions.Contains(permission);
            return Result<bool>.Ok(hasPermission);
        }

        public Result<bool> HasPermission(string token, string resource, string action)
        {
            var permission = $"{resource}:{action}";
            return HasPermission(token, permission);
        }

        public Result<string> GetUserId(string token)
        {
            var validationResult = ValidateToken(token);
            if (!validationResult.Success)
                return Result<string>.Fail(validationResult.Errors);

            return Result<string>.Ok(validationResult.Value.User.Id);
        }

        public Result<string> GetOrganizationId(string token)
        {
            var validationResult = ValidateToken(token);
            if (!validationResult.Success)
                return Result<string>.Fail(validationResult.Errors);

            return Result<string>.Ok(validationResult.Value.Organization.Id);
        }

        private ScopedTokenPayload ExtractPayloadFromClaims(IEnumerable<Claim> claims)
        {
            var claimsList = claims.ToList();
            

            // Extract basic JWT claims
            var sub = claimsList.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
            var jti = claimsList.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;
            var exp = long.Parse(claimsList.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value ?? "0");
            var iat = long.Parse(claimsList.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iat)?.Value ?? "0");

            // Extract user information
            var user = new ScopedTokenUser
            {
                Id = claimsList.FirstOrDefault(c => c.Type == "user_id")?.Value ?? string.Empty,
                Email = claimsList.FirstOrDefault(c => c.Type == "user_email")?.Value ?? string.Empty,
                Name = claimsList.FirstOrDefault(c => c.Type == "user_name")?.Value ?? string.Empty,
                ExternalId = sub
            };

            // Extract organization information
            var organization = new ScopedTokenOrganization
            {
                Id = claimsList.FirstOrDefault(c => c.Type == "org_id")?.Value ?? string.Empty,
                Name = claimsList.FirstOrDefault(c => c.Type == "org_name")?.Value ?? string.Empty,
                Path = claimsList.FirstOrDefault(c => c.Type == "org_path")?.Value ?? string.Empty
            };

            // Extract permissions
            var permissions = claimsList.Where(c => c.Type == "permissions").Select(c => c.Value).ToList();

            // Extract roles
            var roles = claimsList.Where(c => c.Type == "roles")
                .Select(c =>
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(c.Value);
                        var roleData = jsonDoc.RootElement;
                        return new ScopedTokenRole
                        {
                            Id = roleData.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                            Name = roleData.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                            IsInheritable = roleData.TryGetProperty("is_inheritable", out var inheritProp) && inheritProp.GetBoolean()
                        };
                    }
                    catch
                    {
                        return new ScopedTokenRole();
                    }
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .ToList();

            // Extract metadata
            var metadata = new ScopedTokenMetadata
            {
                GeneratedAt = DateTime.TryParse(claimsList.FirstOrDefault(c => c.Type == "generated_at")?.Value, out var genAt) ? genAt : DateTime.UtcNow,
                Source = claimsList.FirstOrDefault(c => c.Type == "source")?.Value ?? "unknown",
                IpAddress = claimsList.FirstOrDefault(c => c.Type == "ip_address")?.Value,
                UserAgent = claimsList.FirstOrDefault(c => c.Type == "user_agent")?.Value,
                SessionId = claimsList.FirstOrDefault(c => c.Type == "session_id")?.Value,
                PermissionCount = int.Parse(claimsList.FirstOrDefault(c => c.Type == "permission_count")?.Value ?? "0"),
                InheritedPermissionCount = int.Parse(claimsList.FirstOrDefault(c => c.Type == "inherited_count")?.Value ?? "0")
            };

            return new ScopedTokenPayload
            {
                Sub = sub,
                Jti = jti,
                Exp = exp,
                Iat = iat,
                User = user,
                Organization = organization,
                Permissions = permissions,
                Roles = roles,
                Scope = claimsList.FirstOrDefault(c => c.Type == "scope")?.Value ?? string.Empty,
                IncludesInherited = bool.Parse(claimsList.FirstOrDefault(c => c.Type == "includes_inherited")?.Value ?? "true"),
                Metadata = metadata
            };
        }

        private static string GenerateJti()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[16];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
        }
    }

}