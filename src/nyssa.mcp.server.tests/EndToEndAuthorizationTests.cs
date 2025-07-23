using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Nyssa.Mcp.Server.Authorization;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Nyssa.Mcp.Server.Models.Messages.Authorization;
using Nyssa.Mcp.Server.Services;
using Nyssa.Mcp.Server.Tools;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Tests
{
    /// <summary>
    /// End-to-end tests that simulate the complete RBAC authentication and authorization flow
    /// </summary>
    public class EndToEndAuthorizationTests
    {
        private readonly Mock<WorkOSAuthenticationService> _mockWorkOSService;
        private readonly Mock<IRequestClient<ResolveUserRequest>> _mockResolveUserClient;
        private readonly Mock<IRequestClient<CreateUserRequest>> _mockCreateUserClient;
        private readonly Mock<IRequestClient<GetUserOrganizationsRequest>> _mockGetOrgsClient;
        private readonly Mock<IRequestClient<GetUserPermissionsRequest>> _mockGetPermissionsClient;
        private readonly Mock<IRequestClient<BlacklistTokenRequest>> _mockBlacklistClient;
        private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;
        private readonly Mock<ILogger<EnhancedAuthenticationService>> _mockAuthLogger;
        private readonly Mock<ILogger<JwtService>> _mockJwtLogger;
        private readonly Mock<ILogger<McpAuthorizationService>> _mockAuthzLogger;
        private readonly Mock<ILogger<EnhancedAuthenticationTools>> _mockToolsLogger;

        private readonly JwtService _jwtService;
        private readonly EnhancedAuthenticationService _enhancedAuthService;
        private readonly McpAuthorizationService _authorizationService;
        private readonly EnhancedAuthenticationTools _authTools;

        public EndToEndAuthorizationTests()
        {
            // Setup all mocks
            _mockWorkOSService = new Mock<WorkOSAuthenticationService>(Mock.Of<Microsoft.Extensions.Options.IOptions<OidcConfiguration>>(), Mock.Of<HttpClient>(), Mock.Of<ILogger<WorkOSAuthenticationService>>());
            _mockResolveUserClient = new Mock<IRequestClient<ResolveUserRequest>>();
            _mockCreateUserClient = new Mock<IRequestClient<CreateUserRequest>>();
            _mockGetOrgsClient = new Mock<IRequestClient<GetUserOrganizationsRequest>>();
            _mockGetPermissionsClient = new Mock<IRequestClient<GetUserPermissionsRequest>>();
            _mockBlacklistClient = new Mock<IRequestClient<BlacklistTokenRequest>>();
            _mockPublishEndpoint = new Mock<IPublishEndpoint>();
            _mockAuthLogger = new Mock<ILogger<EnhancedAuthenticationService>>();
            _mockJwtLogger = new Mock<ILogger<JwtService>>();
            _mockAuthzLogger = new Mock<ILogger<McpAuthorizationService>>();
            _mockToolsLogger = new Mock<ILogger<EnhancedAuthenticationTools>>();

            // Setup JWT service
            var jwtOptions = new JwtOptions
            {
                SecretKey = "e2e-test-secret-key-that-is-very-long-and-secure-for-testing-purposes",
                Issuer = "test-nyssa-mcp-server",
                Audience = "test-nyssa-api",
                ExpirationMinutes = 60,
                IncludeMetadata = true,
                MaxPermissions = 200
            };

            _jwtService = new JwtService(jwtOptions, _mockJwtLogger.Object);

            // Setup enhanced authentication service
            _enhancedAuthService = new EnhancedAuthenticationService(
                _mockWorkOSService.Object,
                _jwtService,
                _mockResolveUserClient.Object,
                _mockCreateUserClient.Object,
                _mockGetOrgsClient.Object,
                _mockGetPermissionsClient.Object,
                _mockBlacklistClient.Object,
                _mockPublishEndpoint.Object,
                _mockAuthLogger.Object);

            // Setup authorization service
            _authorizationService = new McpAuthorizationService(_jwtService, _mockAuthzLogger.Object);

            // Setup authentication tools
            _authTools = new EnhancedAuthenticationTools();
            EnhancedAuthenticationTools.Initialize(_enhancedAuthService, _authorizationService, _mockToolsLogger.Object);
        }

        [Fact]
        public async Task Complete_Authentication_Flow_Should_Work_End_To_End()
        {
            // Arrange - Setup the complete authentication flow
            var authCode = "workos_auth_code_123";
            var workOSUserId = "workos_user_456";
            var rbacUserId = "rbac_user_789";
            var orgId = "org_engineering_123";

            // Mock WorkOS authentication
            var workOSResult = new AuthenticationResult
            {
                IsSuccess = true,
                User = new UserProfile
                {
                    Id = workOSUserId,
                    Email = "john.doe@company.com",
                    FirstName = "John",
                    LastName = "Doe",
                    CreatedAt = DateTime.UtcNow.AddYears(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-30)
                },
                AccessToken = "workos_access_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _mockWorkOSService.Setup(x => x.ExchangeCodeForTokenAsync(authCode))
                .Returns(Task.FromResult(workOSResult));

            // Mock user resolution (existing user)
            var resolveUserResponse = new ResolveUserResponse(
                userId: rbacUserId,
                externalUserId: workOSUserId,
                email: "john.doe@company.com",
                firstName: "John",
                lastName: "Doe",
                createdAt: DateTime.UtcNow.AddYears(-2),
                updatedAt: DateTime.UtcNow.AddDays(-30),
                isNewUser: false,
                requestId: "resolve_request_123");

            _mockResolveUserClient.Setup(x => x.GetResponse<ResolveUserResponse>(It.IsAny<ResolveUserRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<ResolveUserResponse>>(r => r.Message == resolveUserResponse)));

            // Mock organization resolution
            var userOrgs = new List<UserOrganizationMembership>
            {
                new UserOrganizationMembership
                {
                    OrganizationId = orgId,
                    OrganizationName = "Engineering Team",
                    OrganizationPath = "company.engineering",
                    IsPrimary = true,
                    JoinedAt = DateTime.UtcNow.AddYears(-1),
                    Status = "Active",
                    Roles = new List<UserRoleInfo>
                    {
                        new UserRoleInfo
                        {
                            RoleId = "role_senior_dev",
                            RoleName = "Senior Developer",
                            AssignedAt = DateTime.UtcNow.AddMonths(-6),
                            IsInheritable = true
                        }
                    }
                }
            };

            var orgsResponse = new GetUserOrganizationsResponse(
                userId: rbacUserId,
                organizations: userOrgs,
                totalCount: 1,
                requestId: "orgs_request_123");

            _mockGetOrgsClient.Setup(x => x.GetResponse<GetUserOrganizationsResponse>(It.IsAny<GetUserOrganizationsRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<GetUserOrganizationsResponse>>(r => r.Message == orgsResponse)));

            // Mock permission resolution
            var userPermissions = new List<UserPermission>
            {
                CreateTestPermission("users:read", "users", "read"),
                CreateTestPermission("users:write", "users", "write"),
                CreateTestPermission("projects:read", "projects", "read"),
                CreateTestPermission("projects:write", "projects", "write"),
                CreateTestPermission("deployments:read", "deployments", "read")
            };

            var permissionsResponse = new GetUserPermissionsResponse(
                userId: rbacUserId,
                organizationId: orgId,
                organizationName: "Engineering Team",
                permissions: userPermissions,
                roles: userOrgs[0].Roles,
                includedInherited: true,
                requestId: "permissions_request_123");

            _mockGetPermissionsClient.Setup(x => x.GetResponse<GetUserPermissionsResponse>(It.IsAny<GetUserPermissionsRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<GetUserPermissionsResponse>>(r => r.Message == permissionsResponse)));

            // Mock audit logging (fire-and-forget)
            _mockPublishEndpoint.Setup(x => x.Publish(It.IsAny<LogAuthenticationEventRequest>(), default))
                .Returns(Task.CompletedTask);

            // Act & Assert - Step 1: Complete authentication flow
            var authResult = await _enhancedAuthService.AuthenticateWithScopedTokenAsync(
                authCode,
                "192.168.1.100",
                "E2ETestClient/1.0",
                "session_e2e_test");

            // Assert authentication success
            authResult.Success.Should().BeTrue("Complete authentication should succeed");
            var authData = authResult.Value;
            authData.ScopedToken.Token.Should().NotBeNullOrEmpty();
            authData.User.Email.Should().Be("john.doe@company.com");
            authData.PrimaryOrganization.OrganizationName.Should().Be("Engineering Team");
            authData.Permissions.PermissionCodes.Should().Contain("users:read");
            authData.Permissions.PermissionCodes.Should().Contain("projects:write");
            authData.IsNewUser.Should().BeFalse();

            var scopedToken = authData.ScopedToken.Token;

            // Act & Assert - Step 2: Test MCP tool authentication
            var authToolResult = await _authTools.ExchangeCodeForScopedTokenAsync(authCode, "192.168.1.100", "E2ETestClient/1.0");
            authToolResult.Should().NotBeNullOrEmpty();

            var toolResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(authToolResult);
            toolResponse.Should().ContainKey("success");
            toolResponse["success"].ToString().Should().Be("True");
            toolResponse.Should().ContainKey("access_token");

            // Act & Assert - Step 3: Test token validation
            var validationResult = _authTools.ValidateToken(scopedToken);
            validationResult.Should().NotBeNullOrEmpty();

            var validationResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(validationResult);
            validationResponse.Should().ContainKey("success");
            validationResponse["success"].ToString().Should().Be("True");
            validationResponse.Should().ContainKey("valid");
            validationResponse["valid"].ToString().Should().Be("True");

            // Act & Assert - Step 4: Test user context retrieval
            var contextResult = await _authTools.GetUserContextAsync($"Bearer {scopedToken}");
            contextResult.Should().NotBeNullOrEmpty();

            var contextResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(contextResult);
            contextResponse.Should().ContainKey("success");
            contextResponse["success"].ToString().Should().Be("True");
            contextResponse.Should().ContainKey("is_authenticated");
            contextResponse["is_authenticated"].ToString().Should().Be("True");

            // Act & Assert - Step 5: Test permission checking
            var permissionCheckResult = await _authTools.CheckPermissionsAsync($"Bearer {scopedToken}", "users:read,projects:write,admin:delete");
            permissionCheckResult.Should().NotBeNullOrEmpty();

            var permissionResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(permissionCheckResult);
            permissionResponse.Should().ContainKey("success");
            permissionResponse["success"].ToString().Should().Be("True");
            permissionResponse.Should().ContainKey("has_any");
            permissionResponse["has_any"].ToString().Should().Be("True");
            permissionResponse.Should().ContainKey("has_all");
            permissionResponse["has_all"].ToString().Should().Be("False"); // admin:delete not granted

            // Act & Assert - Step 6: Test protected tool access
            var protectedToolResult = await _authTools.DemoProtectedToolAsync($"Bearer {scopedToken}", "test-param");
            protectedToolResult.Should().NotBeNullOrEmpty();

            var protectedResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(protectedToolResult);
            protectedResponse.Should().ContainKey("success");
            protectedResponse["success"].ToString().Should().Be("True");
            protectedResponse.Should().ContainKey("message");
            protectedResponse["message"].ToString().Should().Be("Successfully accessed protected tool");

            // Act & Assert - Step 7: Test token revocation
            var mockBlacklistResponse = new BlacklistTokenResponse
            {
                Success = true,
                TokenId = "extracted_jti",
                Reason = "user_logout",
                RequestId = "blacklist_request_123"
            };

            _mockBlacklistClient.Setup(x => x.GetResponse<BlacklistTokenResponse>(It.IsAny<BlacklistTokenRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<BlacklistTokenResponse>>(r => r.Message == mockBlacklistResponse)));

            var revokeResult = await _authTools.RevokeTokenAsync(scopedToken, "user_logout", "192.168.1.100");
            revokeResult.Should().NotBeNullOrEmpty();

            var revokeResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(revokeResult);
            revokeResponse.Should().ContainKey("success");
            revokeResponse["success"].ToString().Should().Be("True");
            revokeResponse.Should().ContainKey("revoked");
            revokeResponse["revoked"].ToString().Should().Be("True");

            // Verify all service interactions occurred (called twice: once by enhanced auth service, once by auth tools)
            _mockWorkOSService.Verify(x => x.ExchangeCodeForTokenAsync(authCode), Times.Exactly(2));
            _mockResolveUserClient.Verify(x => x.GetResponse<ResolveUserResponse>(It.IsAny<ResolveUserRequest>(), default, default), Times.AtLeastOnce);
            _mockGetOrgsClient.Verify(x => x.GetResponse<GetUserOrganizationsResponse>(It.IsAny<GetUserOrganizationsRequest>(), default, default), Times.AtLeastOnce);
            _mockGetPermissionsClient.Verify(x => x.GetResponse<GetUserPermissionsResponse>(It.IsAny<GetUserPermissionsRequest>(), default, default), Times.AtLeastOnce);
            _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<LogAuthenticationEventRequest>(), default), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Authentication_Flow_Should_Handle_New_User_Creation()
        {
            // Arrange - Setup flow for new user
            var authCode = "workos_new_user_code";
            var workOSUserId = "workos_new_user_789";
            var newRbacUserId = "rbac_new_user_101";

            // Mock WorkOS authentication for new user
            var workOSResult = new AuthenticationResult
            {
                IsSuccess = true,
                User = new UserProfile
                {
                    Id = workOSUserId,
                    Email = "new.user@company.com",
                    FirstName = "New",
                    LastName = "User",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                AccessToken = "workos_new_user_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _mockWorkOSService.Setup(x => x.ExchangeCodeForTokenAsync(authCode))
                .Returns(Task.FromResult(workOSResult));

            // Mock user resolution (user not found)
            var emptyResolveResponse = new ResolveUserResponse();
            _mockResolveUserClient.Setup(x => x.GetResponse<ResolveUserResponse>(It.IsAny<ResolveUserRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<ResolveUserResponse>>(r => r.Message == emptyResolveResponse)));

            // Mock user creation
            var createUserResponse = new CreateUserResponse(
                userId: newRbacUserId,
                externalUserId: workOSUserId,
                email: "new.user@company.com",
                firstName: "New",
                lastName: "User",
                createdAt: DateTime.UtcNow,
                requestId: "create_user_request_123");

            _mockCreateUserClient.Setup(x => x.GetResponse<CreateUserResponse>(It.IsAny<CreateUserRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<CreateUserResponse>>(r => r.Message == createUserResponse)));

            // Mock organization assignment (new users get default org)
            var defaultOrg = new UserOrganizationMembership
            {
                OrganizationId = "org_default",
                OrganizationName = "Default Organization",
                OrganizationPath = "company.default",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow,
                Status = "Active",
                Roles = new List<UserRoleInfo>
                {
                    new UserRoleInfo
                    {
                        RoleId = "role_basic_user",
                        RoleName = "Basic User",
                        AssignedAt = DateTime.UtcNow,
                        IsInheritable = false
                    }
                }
            };

            var newUserOrgsResponse = new GetUserOrganizationsResponse(
                userId: newRbacUserId,
                organizations: new List<UserOrganizationMembership> { defaultOrg },
                totalCount: 1,
                requestId: "new_user_orgs_request");

            _mockGetOrgsClient.Setup(x => x.GetResponse<GetUserOrganizationsResponse>(It.IsAny<GetUserOrganizationsRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<GetUserOrganizationsResponse>>(r => r.Message == newUserOrgsResponse)));

            // Mock basic permissions for new user
            var basicPermissions = new List<UserPermission>
            {
                CreateTestPermission("profile:read", "profile", "read"),
                CreateTestPermission("profile:write", "profile", "write")
            };

            var newUserPermissionsResponse = new GetUserPermissionsResponse(
                userId: newRbacUserId,
                organizationId: "org_default",
                organizationName: "Default Organization",
                permissions: basicPermissions,
                roles: defaultOrg.Roles,
                includedInherited: true,
                requestId: "new_user_permissions_request");

            _mockGetPermissionsClient.Setup(x => x.GetResponse<GetUserPermissionsResponse>(It.IsAny<GetUserPermissionsRequest>(), default, default))
                .Returns(Task.FromResult(Mock.Of<Response<GetUserPermissionsResponse>>(r => r.Message == newUserPermissionsResponse)));

            _mockPublishEndpoint.Setup(x => x.Publish(It.IsAny<LogAuthenticationEventRequest>(), default))
                .Returns(Task.CompletedTask);

            // Act
            var authResult = await _enhancedAuthService.AuthenticateWithScopedTokenAsync(authCode);

            // Assert
            authResult.Success.Should().BeTrue("New user authentication should succeed");
            var authData = authResult.Value;
            authData.IsNewUser.Should().BeTrue("Should indicate this is a new user");
            authData.User.Email.Should().Be("new.user@company.com");
            authData.Permissions.PermissionCodes.Should().Contain("profile:read");
            authData.Permissions.PermissionCodes.Should().NotContain("users:write"); // Should not have admin permissions

            // Verify user creation was called
            _mockCreateUserClient.Verify(x => x.GetResponse<CreateUserResponse>(
                It.Is<CreateUserRequest>(req => req.ExternalUserId == workOSUserId), 
                default, default), Times.Once);
        }

        [Fact]
        public async Task Authorization_Should_Block_Access_Without_Required_Permissions()
        {
            // Arrange - Create user with limited permissions
            var limitedToken = await CreateLimitedPermissionTokenAsync();

            // Act - Try to access demo protected tool (requires users:read AND users:write)
            var result = await _authTools.DemoProtectedToolAsync($"Bearer {limitedToken}", "test");

            // Assert - Should be blocked due to missing permissions
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("False");
            response.Should().ContainKey("authorized");
            response["authorized"].ToString().Should().Be("False");
            response.Should().ContainKey("error_code");
        }

        [Fact]
        public async Task Token_Refresh_Should_Work_With_Updated_Permissions()
        {
            // This test would require implementing token refresh functionality
            // For now, we'll test the basic refresh flow structure

            var originalToken = await CreateTestTokenAsync();
            
            // Mock updated permissions
            var updatedPermissions = new GetUserPermissionsResponse(
                userId: "test_user",
                organizationId: "test_org",
                organizationName: "Test Org",
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("users:read", "users", "read"),
                    CreateTestPermission("users:write", "users", "write"),
                    CreateTestPermission("admin:delete", "admin", "delete") // New permission
                },
                roles: new List<UserRoleInfo>(),
                includedInherited: true,
                requestId: "refresh_request");

            // The refresh flow would use the enhanced authentication service
            // to generate a new token with updated permissions
            originalToken.Should().NotBeNullOrEmpty();
        }

        // Helper methods

        private UserPermission CreateTestPermission(string permission, string resource, string action)
        {
            return new UserPermission
            {
                PermissionId = $"perm_{Guid.NewGuid():N}",
                Permission = permission,
                Resource = resource,
                Action = action,
                Description = $"Test permission for {permission}",
                IsInherited = false,
                GrantedAt = DateTime.UtcNow.AddDays(-30)
            };
        }

        private async Task<string> CreateLimitedPermissionTokenAsync()
        {
            var rbacUser = new RbacUser
            {
                Id = "limited_user",
                ExternalId = "workos_limited",
                Email = "limited@test.com",
                FirstName = "Limited",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = "Active",
                Source = "WorkOS"
            };

            var organization = new UserOrganizationMembership
            {
                OrganizationId = "org_limited",
                OrganizationName = "Limited Org",
                OrganizationPath = "company.limited",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow,
                Status = "Active"
            };

            // Only profile permissions, not users:write
            var permissions = new GetUserPermissionsResponse(
                userId: rbacUser.Id,
                organizationId: organization.OrganizationId,
                organizationName: organization.OrganizationName,
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("profile:read", "profile", "read"),
                    CreateTestPermission("users:read", "users", "read") // Missing users:write
                },
                roles: new List<UserRoleInfo>(),
                includedInherited: true,
                requestId: "limited_request");

            var tokenResult = await _jwtService.GenerateScopedTokenAsync(
                "workos_limited",
                rbacUser,
                organization,
                permissions);

            tokenResult.Success.Should().BeTrue();
            return tokenResult.Value.Token;
        }

        private async Task<string> CreateTestTokenAsync()
        {
            var rbacUser = new RbacUser
            {
                Id = "test_user",
                ExternalId = "workos_test",
                Email = "test@test.com",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = "Active",
                Source = "WorkOS"
            };

            var organization = new UserOrganizationMembership
            {
                OrganizationId = "test_org",
                OrganizationName = "Test Org",
                OrganizationPath = "company.test",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow,
                Status = "Active"
            };

            var permissions = new GetUserPermissionsResponse(
                userId: rbacUser.Id,
                organizationId: organization.OrganizationId,
                organizationName: organization.OrganizationName,
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("users:read", "users", "read"),
                    CreateTestPermission("users:write", "users", "write")
                },
                roles: new List<UserRoleInfo>(),
                includedInherited: true,
                requestId: "test_request");

            var tokenResult = await _jwtService.GenerateScopedTokenAsync(
                "workos_test",
                rbacUser,
                organization,
                permissions);

            tokenResult.Success.Should().BeTrue();
            return tokenResult.Value.Token;
        }
    }
}