using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nyssa.Mcp.Server.Authorization;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Nyssa.Mcp.Server.Services;
using System.Reflection;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Tests
{
    /// <summary>
    /// Comprehensive integration tests that validate the complete RBAC system
    /// </summary>
    public class IntegrationTests
    {
        private readonly JwtService _jwtService;
        private readonly McpAuthorizationService _authorizationService;
        private readonly Mock<ILogger<JwtService>> _jwtLogger;
        private readonly Mock<ILogger<McpAuthorizationService>> _authLogger;

        public IntegrationTests()
        {
            // Setup JWT service with test configuration
            var jwtOptions = new JwtOptions
            {
                SecretKey = "test-secret-key-that-is-at-least-32-characters-long-for-security",
                Issuer = "test-nyssa-mcp-server",
                Audience = "test-nyssa-api",
                ExpirationMinutes = 60,
                IncludeMetadata = true,
                MaxPermissions = 100
            };

            _jwtLogger = new Mock<ILogger<JwtService>>();
            _authLogger = new Mock<ILogger<McpAuthorizationService>>();

            _jwtService = new JwtService(jwtOptions, _jwtLogger.Object);
            _authorizationService = new McpAuthorizationService(_jwtService, _authLogger.Object);
        }

        [Fact]
        public async Task JwtService_Should_Generate_And_Validate_Scoped_Token()
        {
            // Arrange
            var workosUserId = "workos_user_123";
            var rbacUser = new RbacUser
            {
                Id = "rbac_user_456",
                ExternalId = workosUserId,
                Email = "test@example.com",
                FirstName = "John",
                LastName = "Doe",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                Status = "Active",
                Source = "WorkOS"
            };

            var organization = new UserOrganizationMembership
            {
                OrganizationId = "org_789",
                OrganizationName = "Test Organization",
                OrganizationPath = "company.engineering",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow.AddDays(-30),
                Status = "Active"
            };

            var permissions = new GetUserPermissionsResponse(
                userId: rbacUser.Id,
                organizationId: organization.OrganizationId,
                organizationName: organization.OrganizationName,
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("users:read", "users", "read"),
                    CreateTestPermission("users:write", "users", "write"),
                    CreateTestPermission("projects:read", "projects", "read")
                },
                roles: new List<UserRoleInfo>
                {
                    new UserRoleInfo
                    {
                        RoleId = "role_123",
                        RoleName = "Developer",
                        AssignedAt = DateTime.UtcNow.AddDays(-30),
                        IsInheritable = true
                    }
                },
                includedInherited: true,
                requestId: "test_request_123"
            );

            // Act - Generate token
            var tokenResult = await _jwtService.GenerateScopedTokenAsync(
                workosUserId,
                rbacUser,
                organization,
                permissions,
                "192.168.1.1",
                "TestAgent/1.0",
                "session_123");

            // Assert - Token generation
            tokenResult.Success.Should().BeTrue();
            var token = tokenResult.Value;
            token.Token.Should().NotBeNullOrEmpty();
            token.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
            token.Payload.Permissions.Should().Contain("users:read");
            token.Payload.Permissions.Should().Contain("users:write");
            token.Payload.Permissions.Should().Contain("projects:read");

            // Act - Validate token
            var validationResult = _jwtService.ValidateToken(token.Token);

            // Assert - Token validation
            validationResult.Success.Should().BeTrue();
            var payload = validationResult.Value;
            payload.User.Id.Should().Be(rbacUser.Id);
            payload.User.Email.Should().Be(rbacUser.Email);
            payload.Organization.Id.Should().Be(organization.OrganizationId);
            payload.Permissions.Should().HaveCount(3);
            payload.Permissions.Should().Contain("users:read");
            payload.Roles.Should().HaveCount(1);
            payload.Roles[0].Name.Should().Be("Developer");
        }

        [Fact]
        public async Task JwtService_Should_Reject_Invalid_Tokens()
        {
            // Arrange
            var invalidTokens = new[]
            {
                "invalid.token.here",
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalid.signature",
                "",
                "not-a-jwt-at-all"
            };

            foreach (var invalidToken in invalidTokens)
            {
                // Act
                var result = _jwtService.ValidateToken(invalidToken);

                // Assert
                result.Success.Should().BeFalse();
                result.Errors.Should().NotBeEmpty();
            }
        }

        [Fact]
        public async Task AuthorizationService_Should_Create_Valid_Context_From_Token()
        {
            // Arrange - Create a valid token first
            var tokenResult = await CreateTestScopedTokenAsync();
            tokenResult.Success.Should().BeTrue();
            var token = tokenResult.Value.Token;

            // Act
            var contextResult = _authorizationService.CreateAuthorizationContext($"Bearer {token}");

            // Assert
            contextResult.Success.Should().BeTrue();
            var context = contextResult.Value;
            context.IsAuthenticated.Should().BeTrue();
            context.User.Should().NotBeNull();
            context.User!.Email.Should().Be("test@example.com");
            context.Organization.Should().NotBeNull();
            context.Organization!.Name.Should().Be("Test Organization");
            context.Permissions.Should().Contain("users:read");
            context.HasPermission("users:read").Should().BeTrue();
            context.HasPermission("users:write").Should().BeTrue();
            context.HasPermission("admin:delete").Should().BeFalse();
            context.HasRole("Developer").Should().BeTrue();
            context.HasRole("Admin").Should().BeFalse();
        }

        [Fact]
        public async Task AuthorizationService_Should_Handle_Anonymous_Access()
        {
            // Act
            var contextResult = _authorizationService.CreateAuthorizationContext(null);

            // Assert
            contextResult.Success.Should().BeTrue();
            var context = contextResult.Value;
            context.IsAuthenticated.Should().BeFalse();
            context.User.Should().BeNull();
            context.Organization.Should().BeNull();
            context.Permissions.Should().BeEmpty();
        }

        [Fact]
        public async Task AuthorizationService_Should_Authorize_Method_With_Correct_Permissions()
        {
            // Arrange
            var tokenResult = await CreateTestScopedTokenAsync();
            var token = $"Bearer {tokenResult.Value.Token}";
            var method = GetTestMethodWithPermissions("users:read");

            // Act
            var authResult = await _authorizationService.AuthorizeToolAsync(method, token);

            // Assert
            authResult.Success.Should().BeTrue();
            var context = authResult.Value;
            context.IsAuthenticated.Should().BeTrue();
            context.HasPermission("users:read").Should().BeTrue();
        }

        [Fact]
        public async Task AuthorizationService_Should_Reject_Method_With_Missing_Permissions()
        {
            // Arrange
            var tokenResult = await CreateTestScopedTokenAsync();
            var token = $"Bearer {tokenResult.Value.Token}";
            var method = GetTestMethodWithRestrictedPermission(); // admin:delete permission not granted

            // Act
            var authResult = await _authorizationService.AuthorizeToolAsync(method, token);

            // Assert
            authResult.Success.Should().BeFalse();
            authResult.Errors.Should().NotBeEmpty();
            authResult.Errors.First().Code.Should().Be(4104); // MissingPermission error
        }

        [Fact]
        public async Task AuthorizationService_Should_Allow_Anonymous_Methods()
        {
            // Arrange
            var method = GetTestMethodWithAnonymousAccess();

            // Act
            var authResult = await _authorizationService.AuthorizeToolAsync(method, null);

            // Assert
            authResult.Success.Should().BeTrue();
            var context = authResult.Value;
            context.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task AuthorizationService_Should_Reject_Unauthenticated_Protected_Methods()
        {
            // Arrange
            var method = GetTestMethodWithAuthenticationRequired();

            // Act
            var authResult = await _authorizationService.AuthorizeToolAsync(method, null);

            // Assert
            authResult.Success.Should().BeFalse();
            authResult.Errors.Should().NotBeEmpty();
            authResult.Errors.First().Code.Should().Be(4001); // InvalidToken error
        }

        [Fact]
        public async Task Complete_Authorization_Flow_Should_Work_End_To_End()
        {
            // Arrange - Create a realistic scenario
            var workosUserId = "workos_real_user";
            var rbacUser = new RbacUser
            {
                Id = "rbac_user_real",
                ExternalId = workosUserId,
                Email = "realuser@company.com",
                FirstName = "Jane",
                LastName = "Smith",
                CreatedAt = DateTime.UtcNow.AddMonths(-6),
                UpdatedAt = DateTime.UtcNow.AddDays(-7),
                Status = "Active",
                Source = "WorkOS"
            };

            var organization = new UserOrganizationMembership
            {
                OrganizationId = "org_company_engineering",
                OrganizationName = "Engineering Team",
                OrganizationPath = "company.engineering",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow.AddMonths(-6),
                Status = "Active"
            };

            var permissions = new GetUserPermissionsResponse(
                userId: rbacUser.Id,
                organizationId: organization.OrganizationId,
                organizationName: organization.OrganizationName,
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("users:read", "users", "read"),
                    CreateTestPermission("projects:read", "projects", "read"),
                    CreateTestPermission("projects:write", "projects", "write"),
                    CreateTestPermission("deployments:read", "deployments", "read")
                },
                roles: new List<UserRoleInfo>
                {
                    new UserRoleInfo
                    {
                        RoleId = "role_senior_dev",
                        RoleName = "Senior Developer",
                        AssignedAt = DateTime.UtcNow.AddMonths(-3),
                        IsInheritable = true
                    }
                },
                includedInherited: true,
                requestId: "e2e_test_request"
            );

            // Step 1: Generate scoped token (simulating successful authentication)
            var tokenResult = await _jwtService.GenerateScopedTokenAsync(
                workosUserId,
                rbacUser,
                organization,
                permissions,
                "10.0.0.100",
                "NyssaClient/2.0",
                "session_e2e_test");

            tokenResult.Success.Should().BeTrue("Token generation should succeed");
            var token = tokenResult.Value;

            // Step 2: Validate the token
            var validationResult = _jwtService.ValidateToken(token.Token);
            validationResult.Success.Should().BeTrue("Token validation should succeed");

            // Step 3: Create authorization context
            var contextResult = _authorizationService.CreateAuthorizationContext($"Bearer {token.Token}");
            contextResult.Success.Should().BeTrue("Authorization context creation should succeed");
            var context = contextResult.Value;

            // Step 4: Test various authorization scenarios
            
            // Should allow access to methods user has permissions for
            var allowedMethod = GetTestMethodWithPermissions("projects:read");
            var allowedResult = await _authorizationService.AuthorizeToolAsync(allowedMethod, $"Bearer {token.Token}");
            allowedResult.Success.Should().BeTrue("Should allow access to projects:read");

            // Should deny access to methods user doesn't have permissions for
            var deniedMethod = GetTestMethodWithRestrictedPermission(); // admin:delete permission not granted
            var deniedResult = await _authorizationService.AuthorizeToolAsync(deniedMethod, $"Bearer {token.Token}");
            deniedResult.Success.Should().BeFalse("Should deny access to admin:delete");

            // Should allow anonymous methods
            var anonymousMethod = GetTestMethodWithAnonymousAccess();
            var anonymousResult = await _authorizationService.AuthorizeToolAsync(anonymousMethod, $"Bearer {token.Token}");
            anonymousResult.Success.Should().BeTrue("Should allow anonymous methods even with token");

            // Step 5: Test permission checking methods
            context.HasPermission("projects:write").Should().BeTrue();
            context.HasPermission("admin:delete").Should().BeFalse();
            context.HasRole("Senior Developer").Should().BeTrue();
            context.HasRole("Admin").Should().BeFalse();

            // Step 6: Test token extraction (JTI)
            var jtiResult = _jwtService.ExtractJti(token.Token);
            jtiResult.Success.Should().BeTrue();
            jtiResult.Value.Should().NotBeNullOrEmpty();

            // Step 7: Test user and organization extraction
            var userIdResult = _jwtService.GetUserId(token.Token);
            userIdResult.Success.Should().BeTrue();
            userIdResult.Value.Should().Be(rbacUser.Id);

            var orgIdResult = _jwtService.GetOrganizationId(token.Token);
            orgIdResult.Success.Should().BeTrue();
            orgIdResult.Value.Should().Be(organization.OrganizationId);
        }

        [Fact]
        public void Authorization_Attributes_Should_Be_Detected_Correctly()
        {
            // Test permission detection
            var permissionMethod = GetTestMethodWithPermissions("users:read", "users:write");
            _authorizationService.HasPermissionRequirements(permissionMethod).Should().BeTrue();
            var requiredPermissions = _authorizationService.GetRequiredPermissions(permissionMethod);
            requiredPermissions.Should().Contain("users:read");
            requiredPermissions.Should().Contain("users:write");

            // Test role detection
            var roleMethod = GetTestMethodWithRole("Admin");
            var requiredRoles = _authorizationService.GetRequiredRoles(roleMethod);
            requiredRoles.Should().Contain("Admin");

            // Test anonymous detection
            var anonymousMethod = GetTestMethodWithAnonymousAccess();
            anonymousMethod.AllowsAnonymousAccess().Should().BeTrue();
            
            // Test authentication requirement detection
            var authMethod = GetTestMethodWithAuthenticationRequired();
            authMethod.RequiresAuthentication().Should().BeTrue();
        }

        // Helper methods for creating test data

        private async Task<Result<ScopedToken>> CreateTestScopedTokenAsync()
        {
            var workosUserId = "workos_test_user";
            var rbacUser = new RbacUser
            {
                Id = "rbac_test_user",
                ExternalId = workosUserId,
                Email = "test@example.com",
                FirstName = "Test",
                LastName = "User",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                UpdatedAt = DateTime.UtcNow,
                Status = "Active",
                Source = "WorkOS"
            };

            var organization = new UserOrganizationMembership
            {
                OrganizationId = "org_test",
                OrganizationName = "Test Organization",
                OrganizationPath = "test.org",
                IsPrimary = true,
                JoinedAt = DateTime.UtcNow.AddDays(-30),
                Status = "Active"
            };

            var permissions = new GetUserPermissionsResponse(
                userId: rbacUser.Id,
                organizationId: organization.OrganizationId,
                organizationName: organization.OrganizationName,
                permissions: new List<UserPermission>
                {
                    CreateTestPermission("users:read", "users", "read"),
                    CreateTestPermission("users:write", "users", "write"),
                    CreateTestPermission("projects:read", "projects", "read")
                },
                roles: new List<UserRoleInfo>
                {
                    new UserRoleInfo
                    {
                        RoleId = "role_developer",
                        RoleName = "Developer",
                        AssignedAt = DateTime.UtcNow.AddDays(-30),
                        IsInheritable = true
                    }
                },
                includedInherited: true,
                requestId: "test_request"
            );

            return await _jwtService.GenerateScopedTokenAsync(
                workosUserId,
                rbacUser,
                organization,
                permissions,
                "127.0.0.1",
                "TestAgent/1.0",
                "test_session");
        }

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

        // Create test methods with various authorization attributes
        private MethodInfo GetTestMethodWithPermissions(params string[] permissions)
        {
            var type = typeof(TestToolsClass);
            var methodName = permissions.Length == 1 ? "SinglePermissionMethod" : "MultiplePermissionMethod";
            return type.GetMethod(methodName) ?? throw new InvalidOperationException($"Method {methodName} not found");
        }

        private MethodInfo GetTestMethodWithRole(string role)
        {
            var type = typeof(TestToolsClass);
            return type.GetMethod("RoleMethod") ?? throw new InvalidOperationException("RoleMethod not found");
        }

        private MethodInfo GetTestMethodWithAnonymousAccess()
        {
            var type = typeof(TestToolsClass);
            return type.GetMethod("AnonymousMethod") ?? throw new InvalidOperationException("AnonymousMethod not found");
        }

        private MethodInfo GetTestMethodWithAuthenticationRequired()
        {
            var type = typeof(TestToolsClass);
            return type.GetMethod("AuthenticatedMethod") ?? throw new InvalidOperationException("AuthenticatedMethod not found");
        }

        private MethodInfo GetTestMethodWithRestrictedPermission()
        {
            var type = typeof(TestToolsClass);
            return type.GetMethod("RestrictedMethod") ?? throw new InvalidOperationException("RestrictedMethod not found");
        }

        // Test class with various authorization attributes
        private class TestToolsClass
        {
            [McpRequirePermission("users:read")]
            public void SinglePermissionMethod() { }

            [McpRequirePermission("users:read")]
            [McpRequirePermission("users:write")]
            public void MultiplePermissionMethod() { }

            [McpRequireRole("Admin")]
            public void RoleMethod() { }

            [McpAllowAnonymous]
            public void AnonymousMethod() { }

            [McpRequireAuthentication]
            public void AuthenticatedMethod() { }

            [McpRequirePermission("admin:delete")]
            public void RestrictedMethod() { }
        }
    }
}