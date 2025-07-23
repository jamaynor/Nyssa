using MassTransit;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Nyssa.Mcp.Server.Models.Messages.Authorization;
using Nyssa.Mcp.Server.Services;

namespace Nyssa.Mcp.Server.Services
{
    /// <summary>
    /// Enhanced authentication service that integrates WorkOS authentication with RBAC permissions
    /// and generates scoped JWT tokens
    /// </summary>
    public interface IEnhancedAuthenticationService
    {
        /// <summary>
        /// Complete authentication flow: WorkOS token exchange -> RBAC integration -> Scoped JWT generation
        /// </summary>
        Task<Result<ScopedTokenAuthenticationResult>> AuthenticateWithScopedTokenAsync(
            string authorizationCode,
            string? ipAddress = null,
            string? userAgent = null,
            string? sessionId = null);

        /// <summary>
        /// Refresh a scoped token (if the underlying permissions have changed)
        /// </summary>
        Task<Result<ScopedToken>> RefreshScopedTokenAsync(
            string currentToken,
            string? ipAddress = null,
            string? userAgent = null);

        /// <summary>
        /// Validate a scoped token and return user information
        /// </summary>
        Result<ScopedTokenPayload> ValidateScopedToken(string token);

        /// <summary>
        /// Revoke a scoped token (blacklist it)
        /// </summary>
        Task<Result> RevokeScopedTokenAsync(
            string token,
            string reason = "user_logout",
            string? requestedBy = null,
            string? ipAddress = null);

        /// <summary>
        /// Build WorkOS authorization URL (pass-through to existing service)
        /// </summary>
        string BuildAuthorizationUrl();
    }

    /// <summary>
    /// Result of complete authentication with scoped token
    /// </summary>
    public record ScopedTokenAuthenticationResult
    {
        /// <summary>
        /// The generated scoped JWT token
        /// </summary>
        public ScopedToken ScopedToken { get; init; } = new();

        /// <summary>
        /// RBAC user information
        /// </summary>
        public RbacUser User { get; init; } = new();

        /// <summary>
        /// User's primary organization
        /// </summary>
        public UserOrganizationMembership PrimaryOrganization { get; init; } = new();

        /// <summary>
        /// User's permissions in the primary organization
        /// </summary>
        public GetUserPermissionsResponse Permissions { get; init; } = new();

        /// <summary>
        /// Whether this is a new user (first-time login)
        /// </summary>
        public bool IsNewUser { get; init; }

        /// <summary>
        /// Original WorkOS user profile
        /// </summary>
        public UserProfile WorkOSProfile { get; init; } = new();

        public ScopedTokenAuthenticationResult() { }

        public ScopedTokenAuthenticationResult(
            ScopedToken scopedToken,
            RbacUser user,
            UserOrganizationMembership primaryOrganization,
            GetUserPermissionsResponse permissions,
            bool isNewUser,
            UserProfile workOSProfile)
        {
            ScopedToken = scopedToken;
            User = user;
            PrimaryOrganization = primaryOrganization;
            Permissions = permissions;
            IsNewUser = isNewUser;
            WorkOSProfile = workOSProfile;
        }
    }

    /// <summary>
    /// Implementation of enhanced authentication service
    /// </summary>
    public class EnhancedAuthenticationService : IEnhancedAuthenticationService
    {
        private readonly WorkOSAuthenticationService _workOSService;
        private readonly IJwtService _jwtService;
        private readonly IRequestClient<ResolveUserRequest> _resolveUserClient;
        private readonly IRequestClient<CreateUserRequest> _createUserClient;
        private readonly IRequestClient<GetUserOrganizationsRequest> _getUserOrganizationsClient;
        private readonly IRequestClient<GetUserPermissionsRequest> _getUserPermissionsClient;
        private readonly IRequestClient<BlacklistTokenRequest> _blacklistTokenClient;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<EnhancedAuthenticationService> _logger;

        public EnhancedAuthenticationService(
            WorkOSAuthenticationService workOSService,
            IJwtService jwtService,
            IRequestClient<ResolveUserRequest> resolveUserClient,
            IRequestClient<CreateUserRequest> createUserClient,
            IRequestClient<GetUserOrganizationsRequest> getUserOrganizationsClient,
            IRequestClient<GetUserPermissionsRequest> getUserPermissionsClient,
            IRequestClient<BlacklistTokenRequest> blacklistTokenClient,
            IPublishEndpoint publishEndpoint,
            ILogger<EnhancedAuthenticationService> logger)
        {
            _workOSService = workOSService ?? throw new ArgumentNullException(nameof(workOSService));
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _resolveUserClient = resolveUserClient ?? throw new ArgumentNullException(nameof(resolveUserClient));
            _createUserClient = createUserClient ?? throw new ArgumentNullException(nameof(createUserClient));
            _getUserOrganizationsClient = getUserOrganizationsClient ?? throw new ArgumentNullException(nameof(getUserOrganizationsClient));
            _getUserPermissionsClient = getUserPermissionsClient ?? throw new ArgumentNullException(nameof(getUserPermissionsClient));
            _blacklistTokenClient = blacklistTokenClient ?? throw new ArgumentNullException(nameof(blacklistTokenClient));
            _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<ScopedTokenAuthenticationResult>> AuthenticateWithScopedTokenAsync(
            string authorizationCode,
            string? ipAddress = null,
            string? userAgent = null,
            string? sessionId = null)
        {
            try
            {
                _logger.LogInformation("Starting enhanced authentication flow for authorization code");

                // Step 1: Exchange authorization code with WorkOS
                _logger.LogInformation("Step 1: Exchanging authorization code with WorkOS");
                var workOSResult = await _workOSService.ExchangeCodeForTokenAsync(authorizationCode);
                
                if (!workOSResult.IsSuccess || workOSResult.User == null)
                {
                    _logger.LogWarning("WorkOS authentication failed: {Error}", workOSResult.ErrorMessage);
                    return Result<ScopedTokenAuthenticationResult>.Fail(RbacErrors.Authentication.WorkOsTokenExchangeFailed);
                }

                var workOSUser = workOSResult.User;
                _logger.LogInformation("WorkOS authentication successful for user {WorkOSUserId}", workOSUser.Id);

                // Step 2: Resolve or create user in RBAC system
                _logger.LogInformation("Step 2: Resolving user in RBAC system");
                var userResolutionResult = await ResolveOrCreateUserAsync(workOSUser);
                if (!userResolutionResult.Success)
                {
                    _logger.LogError("User resolution failed: {Errors}", string.Join(", ", userResolutionResult.Errors.Select(e => e.Text)));
                    return Result<ScopedTokenAuthenticationResult>.Fail(userResolutionResult.Errors);
                }

                var (rbacUser, isNewUser) = userResolutionResult.Value;
                _logger.LogInformation("User resolution successful - RBAC User ID: {RbacUserId}, IsNew: {IsNewUser}", 
                    rbacUser.Id, isNewUser);

                // Step 3: Get user organizations
                _logger.LogInformation("Step 3: Getting user organizations");
                var organizationsResult = await GetUserOrganizationsAsync(rbacUser.Id);
                if (!organizationsResult.Success)
                {
                    _logger.LogError("Failed to get user organizations: {Errors}", string.Join(", ", organizationsResult.Errors.Select(e => e.Text)));
                    return Result<ScopedTokenAuthenticationResult>.Fail(organizationsResult.Errors);
                }

                var organizations = organizationsResult.Value;
                if (organizations.Organizations.Count == 0)
                {
                    _logger.LogWarning("User {UserId} has no organization memberships", rbacUser.Id);
                    return Result<ScopedTokenAuthenticationResult>.Fail(RbacErrors.RbacValidation.NoOrganizationMembership);
                }

                var primaryOrganization = organizations.PrimaryOrganization ?? organizations.Organizations.First();
                _logger.LogInformation("Primary organization selected: {OrgId} ({OrgName})", 
                    primaryOrganization.OrganizationId, primaryOrganization.OrganizationName);

                // Step 4: Get user permissions for primary organization
                _logger.LogInformation("Step 4: Getting user permissions for organization {OrgId}", primaryOrganization.OrganizationId);
                var permissionsResult = await GetUserPermissionsAsync(rbacUser.Id, primaryOrganization.OrganizationId);
                if (!permissionsResult.Success)
                {
                    _logger.LogError("Failed to get user permissions: {Errors}", string.Join(", ", permissionsResult.Errors.Select(e => e.Text)));
                    return Result<ScopedTokenAuthenticationResult>.Fail(permissionsResult.Errors);
                }

                var permissions = permissionsResult.Value;
                _logger.LogInformation("Retrieved {PermissionCount} permissions for user", permissions.Permissions.Count);

                // Step 5: Generate scoped JWT token
                _logger.LogInformation("Step 5: Generating scoped JWT token");
                var tokenResult = await _jwtService.GenerateScopedTokenAsync(
                    workOSUser.Id,
                    rbacUser,
                    primaryOrganization,
                    permissions,
                    ipAddress,
                    userAgent,
                    sessionId);

                if (!tokenResult.Success)
                {
                    _logger.LogError("Failed to generate scoped token: {Errors}", string.Join(", ", tokenResult.Errors.Select(e => e.Text)));
                    return Result<ScopedTokenAuthenticationResult>.Fail(tokenResult.Errors);
                }

                var scopedToken = tokenResult.Value;

                // Step 6: Log authentication event
                _logger.LogInformation("Step 6: Logging authentication event");
                await LogAuthenticationEventAsync(rbacUser, primaryOrganization, scopedToken, ipAddress, isNewUser);

                // Create result
                var result = new ScopedTokenAuthenticationResult(
                    scopedToken,
                    rbacUser,
                    primaryOrganization,
                    permissions,
                    isNewUser,
                    workOSUser);

                _logger.LogInformation("Enhanced authentication completed successfully for user {UserId}", rbacUser.Id);
                return Result<ScopedTokenAuthenticationResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enhanced authentication failed: {Message}", ex.Message);
                return Result<ScopedTokenAuthenticationResult>.Fail(RbacErrors.ExternalService.WorkOsApiError);
            }
        }

        public async Task<Result<ScopedToken>> RefreshScopedTokenAsync(
            string currentToken,
            string? ipAddress = null,
            string? userAgent = null)
        {
            try
            {
                // Validate current token to get user information
                var validationResult = _jwtService.ValidateToken(currentToken);
                if (!validationResult.Success)
                {
                    return Result<ScopedToken>.Fail(validationResult.Errors);
                }

                var currentPayload = validationResult.Value;
                _logger.LogInformation("Refreshing scoped token for user {UserId}", currentPayload.User.Id);

                // Get current user information from RBAC
                var userRequest = new ResolveUserRequest
                {
                    WorkOSUserId = currentPayload.Sub,
                    RequestId = Guid.NewGuid().ToString()
                };

                var userResponse = await _resolveUserClient.GetResponse<ResolveUserResponse>(userRequest);
                if (string.IsNullOrEmpty(userResponse.Message.UserId))
                {
                    return Result<ScopedToken>.Fail(RbacErrors.Authentication.UserNotFound);
                }

                // Convert response to RbacUser
                var rbacUser = new RbacUser
                {
                    Id = userResponse.Message.UserId,
                    ExternalId = userResponse.Message.ExternalUserId,
                    Email = userResponse.Message.Email,
                    FirstName = userResponse.Message.FirstName,
                    LastName = userResponse.Message.LastName,
                    CreatedAt = userResponse.Message.CreatedAt,
                    UpdatedAt = userResponse.Message.UpdatedAt,
                    Status = "Active",
                    Source = "WorkOS"
                };

                // Get current permissions
                var permissionsResult = await GetUserPermissionsAsync(rbacUser.Id, currentPayload.Organization.Id);
                if (!permissionsResult.Success)
                {
                    return Result<ScopedToken>.Fail(permissionsResult.Errors);
                }

                // Get organization info
                var organizationsResult = await GetUserOrganizationsAsync(rbacUser.Id);
                if (!organizationsResult.Success)
                {
                    return Result<ScopedToken>.Fail(organizationsResult.Errors);
                }

                var primaryOrganization = organizationsResult.Value.Organizations
                    .FirstOrDefault(o => o.OrganizationId == currentPayload.Organization.Id);

                if (primaryOrganization == null)
                {
                    return Result<ScopedToken>.Fail(RbacErrors.Authorization.OrganizationAccessDenied);
                }

                // Generate new token
                var newTokenResult = await _jwtService.GenerateScopedTokenAsync(
                    currentPayload.Sub,
                    rbacUser,
                    primaryOrganization,
                    permissionsResult.Value,
                    ipAddress,
                    userAgent,
                    currentPayload.Metadata.SessionId);

                if (!newTokenResult.Success)
                {
                    return Result<ScopedToken>.Fail(newTokenResult.Errors);
                }

                // Blacklist the old token
                var jtiResult = _jwtService.ExtractJti(currentToken);
                if (jtiResult.Success)
                {
                    await BlacklistTokenInternalAsync(jtiResult.Value, "token_refresh", rbacUser.Id, ipAddress);
                }

                _logger.LogInformation("Token refresh completed for user {UserId}", rbacUser.Id);
                return newTokenResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed: {Message}", ex.Message);
                return Result<ScopedToken>.Fail(RbacErrors.ExternalService.JwtValidationError);
            }
        }

        public Result<ScopedTokenPayload> ValidateScopedToken(string token)
        {
            return _jwtService.ValidateToken(token);
        }

        public async Task<Result> RevokeScopedTokenAsync(
            string token,
            string reason = "user_logout",
            string? requestedBy = null,
            string? ipAddress = null)
        {
            try
            {
                var jtiResult = _jwtService.ExtractJti(token);
                if (!jtiResult.Success)
                {
                    return Result.Fail(jtiResult.Errors);
                }

                var validationResult = _jwtService.ValidateToken(token);
                string? userId = validationResult.Success ? validationResult.Value.User.Id : null;

                await BlacklistTokenInternalAsync(jtiResult.Value, reason, userId, ipAddress, requestedBy);

                _logger.LogInformation("Token revoked successfully: {Jti}", jtiResult.Value);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token revocation failed: {Message}", ex.Message);
                return Result.Fail(RbacErrors.Authorization.TokenBlacklistFailed);
            }
        }

        public string BuildAuthorizationUrl()
        {
            return _workOSService.BuildAuthorizationUrl();
        }

        // Private helper methods

        private async Task<Result<(RbacUser User, bool IsNewUser)>> ResolveOrCreateUserAsync(UserProfile workOSUser)
        {
            try
            {
                // Try to resolve existing user
                var resolveRequest = new ResolveUserRequest
                {
                    WorkOSUserId = workOSUser.Id,
                    Email = workOSUser.Email,
                    FirstName = workOSUser.FirstName,
                    LastName = workOSUser.LastName,
                    RequestId = Guid.NewGuid().ToString()
                };

                var resolveResponse = await _resolveUserClient.GetResponse<ResolveUserResponse>(resolveRequest);
                
                // If user exists, convert to RbacUser and return
                if (!string.IsNullOrEmpty(resolveResponse.Message.UserId))
                {
                    var existingUser = new RbacUser
                    {
                        Id = resolveResponse.Message.UserId,
                        ExternalId = resolveResponse.Message.ExternalUserId,
                        Email = resolveResponse.Message.Email,
                        FirstName = resolveResponse.Message.FirstName,
                        LastName = resolveResponse.Message.LastName,
                        CreatedAt = resolveResponse.Message.CreatedAt,
                        UpdatedAt = resolveResponse.Message.UpdatedAt,
                        Status = "Active",
                        Source = "WorkOS"
                    };

                    return Result<(RbacUser, bool)>.Ok((existingUser, resolveResponse.Message.IsNewUser));
                }

                // User not found, create new user
                _logger.LogInformation("User not found, creating new user for WorkOS ID {WorkOSUserId}", workOSUser.Id);
                
                var createRequest = new CreateUserRequest
                {
                    ExternalUserId = workOSUser.Id,
                    Email = workOSUser.Email,
                    FirstName = workOSUser.FirstName,
                    LastName = workOSUser.LastName,
                    Source = "WorkOS",
                    RequestId = Guid.NewGuid().ToString()
                };

                var createResponse = await _createUserClient.GetResponse<CreateUserResponse>(createRequest);
                
                if (!createResponse.Message.Success)
                {
                    _logger.LogError("User creation failed: {ErrorMessage}", createResponse.Message.ErrorMessage);
                    return Result<(RbacUser, bool)>.Fail(RbacErrors.RbacValidation.UserCreationFailed);
                }

                // Convert CreateUserResponse to RbacUser
                var newUser = new RbacUser
                {
                    Id = createResponse.Message.UserId,
                    ExternalId = createResponse.Message.ExternalUserId,
                    Email = createResponse.Message.Email,
                    FirstName = createResponse.Message.FirstName,
                    LastName = createResponse.Message.LastName,
                    CreatedAt = createResponse.Message.CreatedAt,
                    UpdatedAt = createResponse.Message.CreatedAt,
                    Status = createResponse.Message.Status,
                    Source = createResponse.Message.Source
                };

                return Result<(RbacUser, bool)>.Ok((newUser, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve or create user for WorkOS ID {WorkOSUserId}", workOSUser.Id);
                return Result<(RbacUser, bool)>.Fail(RbacErrors.RbacValidation.UserCreationFailed);
            }
        }

        private async Task<Result<GetUserOrganizationsResponse>> GetUserOrganizationsAsync(string userId)
        {
            var request = new GetUserOrganizationsRequest
            {
                UserId = userId,
                IncludeHierarchy = false,
                RequestId = Guid.NewGuid().ToString()
            };

            var response = await _getUserOrganizationsClient.GetResponse<GetUserOrganizationsResponse>(request);
            return Result<GetUserOrganizationsResponse>.Ok(response.Message);
        }

        private async Task<Result<GetUserPermissionsResponse>> GetUserPermissionsAsync(string userId, string organizationId)
        {
            var request = new GetUserPermissionsRequest
            {
                UserId = userId,
                OrganizationId = organizationId,
                IncludeInherited = true,
                IncludeMetadata = false,
                RequestId = Guid.NewGuid().ToString()
            };

            var response = await _getUserPermissionsClient.GetResponse<GetUserPermissionsResponse>(request);
            return Result<GetUserPermissionsResponse>.Ok(response.Message);
        }

        private async Task LogAuthenticationEventAsync(
            RbacUser user,
            UserOrganizationMembership organization,
            ScopedToken token,
            string? ipAddress,
            bool isNewUser)
        {
            try
            {
                var eventRequest = LogAuthenticationEventRequest.CreateSuccess(
                    eventType: isNewUser ? "user_first_login" : "user_login",
                    details: $"{{\"method\":\"workos\",\"token_jti\":\"{token.Payload.Jti}\",\"permissions_count\":{token.Payload.Permissions.Count}}}",
                    userId: user.Id,
                    externalUserId: user.ExternalId,
                    organizationId: organization.OrganizationId,
                    sessionId: token.Metadata.SessionId,
                    ipAddress: ipAddress ?? "unknown");

                await _publishEndpoint.Publish(eventRequest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log authentication event for user {UserId}", user.Id);
                // Don't fail the entire authentication flow for audit logging issues
            }
        }

        private async Task BlacklistTokenInternalAsync(
            string tokenId,
            string reason,
            string? userId = null,
            string? ipAddress = null,
            string? requestedBy = null)
        {
            try
            {
                var blacklistRequest = new BlacklistTokenRequest
                {
                    TokenId = tokenId,
                    Reason = reason,
                    UserId = userId,
                    IpAddress = ipAddress,
                    RequestedBy = requestedBy ?? userId,
                    Source = "enhanced_auth",
                    RequestId = Guid.NewGuid().ToString()
                };

                await _blacklistTokenClient.GetResponse<BlacklistTokenResponse>(blacklistRequest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to blacklist token {TokenId}", tokenId);
                // Don't fail for blacklisting issues in refresh scenarios
            }
        }
    }
}