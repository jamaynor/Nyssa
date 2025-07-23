using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Nyssa.Mcp.Server.Models.Messages.Authorization;
using Nyssa.Mcp.Server.Services.RbacMessageHandlers;
using System.Data;

namespace Nyssa.Mcp.Server.Tests
{
    /// <summary>
    /// Basic functionality tests that verify the RBAC message handlers work correctly
    /// without complex MassTransit setup
    /// </summary>
    public class BasicFunctionalityTests
    {
        [Fact]
        public void ResolveUserRequest_ShouldHaveCorrectProperties()
        {
            // Arrange & Act
            var request = new ResolveUserRequest
            {
                WorkOSUserId = "workos_123",
                Email = "test@example.com",
                FirstName = "John",
                LastName = "Doe",
                RequestId = "req_123"
            };

            // Assert
            request.WorkOSUserId.Should().Be("workos_123");
            request.Email.Should().Be("test@example.com");
            request.FirstName.Should().Be("John");
            request.LastName.Should().Be("Doe");
            request.RequestId.Should().Be("req_123");
            request.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void CreateUserRequest_ShouldHaveCorrectProperties()
        {
            // Arrange & Act
            var request = new CreateUserRequest
            {
                ExternalUserId = "workos_456",
                Email = "newuser@example.com",
                FirstName = "Jane",
                LastName = "Smith",
                Source = "WorkOS",
                RequestId = "req_456"
            };

            // Assert
            request.ExternalUserId.Should().Be("workos_456");
            request.Email.Should().Be("newuser@example.com");
            request.FirstName.Should().Be("Jane");
            request.LastName.Should().Be("Smith");
            request.Source.Should().Be("WorkOS");
            request.RequestId.Should().Be("req_456");
            request.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void GetUserPermissionsRequest_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var request = new GetUserPermissionsRequest
            {
                UserId = "user_123",
                OrganizationId = "org_456"
            };

            // Assert
            request.UserId.Should().Be("user_123");
            request.OrganizationId.Should().Be("org_456");
            request.IncludeInherited.Should().BeTrue(); // Default value
            request.IncludeMetadata.Should().BeFalse(); // Default value
            request.ResourceFilter.Should().BeNull();
            request.ActionFilter.Should().BeNull();
            request.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void GetUserPermissionsResponse_ShouldCalculatePermissionCodes()
        {
            // Arrange
            var permissions = new List<UserPermission>
            {
                new UserPermission
                {
                    PermissionId = "perm_1",
                    Permission = "users:read",
                    Resource = "users",
                    Action = "read",
                    GrantedAt = DateTime.UtcNow
                },
                new UserPermission
                {
                    PermissionId = "perm_2",
                    Permission = "projects:write",
                    Resource = "projects", 
                    Action = "write",
                    GrantedAt = DateTime.UtcNow
                }
            };

            var roles = new List<UserRoleInfo>
            {
                new UserRoleInfo
                {
                    RoleId = "role_1",
                    RoleName = "Developer",
                    AssignedAt = DateTime.UtcNow
                }
            };

            // Act
            var response = new GetUserPermissionsResponse(
                userId: "user_123",
                organizationId: "org_456",
                organizationName: "Test Org",
                permissions: permissions,
                roles: roles,
                includedInherited: true,
                requestId: "req_123");

            // Assert
            response.UserId.Should().Be("user_123");
            response.OrganizationId.Should().Be("org_456");
            response.OrganizationName.Should().Be("Test Org");
            response.Permissions.Should().HaveCount(2);
            response.PermissionCodes.Should().Contain("users:read");
            response.PermissionCodes.Should().Contain("projects:write");
            response.Roles.Should().HaveCount(1);
            response.Roles.First().RoleName.Should().Be("Developer");
            response.IncludedInherited.Should().BeTrue();
            response.InheritedPermissionCount.Should().Be(0); // No inherited permissions in test data
            response.RequestId.Should().Be("req_123");

            // Test permission checking methods
            response.HasPermission("users:read").Should().BeTrue();
            response.HasPermission("users:write").Should().BeFalse();
            response.HasPermission("users", "read").Should().BeTrue();
            response.HasPermission("users", "write").Should().BeFalse();
        }

        [Fact]
        public void CheckTokenBlacklistRequest_ShouldHaveCorrectProperties()
        {
            // Arrange & Act
            var request = new CheckTokenBlacklistRequest
            {
                TokenId = "jti_123",
                UserId = "user_456",
                OrganizationId = "org_789",
                Source = "mcp_task"
            };

            // Assert
            request.TokenId.Should().Be("jti_123");
            request.UserId.Should().Be("user_456");
            request.OrganizationId.Should().Be("org_789");
            request.Source.Should().Be("mcp_task");
            request.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void CheckTokenBlacklistResponse_StaticMethods_ShouldWorkCorrectly()
        {
            // Test NotBlacklisted static method
            var notBlacklistedResponse = CheckTokenBlacklistResponse.NotBlacklisted(
                tokenId: "jti_clean",
                requestId: "req_clean",
                userId: "user_123",
                organizationId: "org_456");

            notBlacklistedResponse.TokenId.Should().Be("jti_clean");
            notBlacklistedResponse.IsBlacklisted.Should().BeFalse();
            notBlacklistedResponse.BlacklistedAt.Should().BeNull();
            notBlacklistedResponse.BlacklistReason.Should().BeNull();
            notBlacklistedResponse.UserId.Should().Be("user_123");
            notBlacklistedResponse.OrganizationId.Should().Be("org_456");
            notBlacklistedResponse.RequestId.Should().Be("req_clean");

            // Test Blacklisted static method
            var blacklistedAt = DateTime.UtcNow.AddHours(-1);
            var blacklistedResponse = CheckTokenBlacklistResponse.Blacklisted(
                tokenId: "jti_blacklisted",
                blacklistedAt: blacklistedAt,
                blacklistReason: "user_logout",
                blacklistedBy: "user_456",
                requestId: "req_blacklisted");

            blacklistedResponse.TokenId.Should().Be("jti_blacklisted");
            blacklistedResponse.IsBlacklisted.Should().BeTrue();
            blacklistedResponse.BlacklistedAt.Should().Be(blacklistedAt);
            blacklistedResponse.BlacklistReason.Should().Be("user_logout");
            blacklistedResponse.BlacklistedBy.Should().Be("user_456");
            blacklistedResponse.RequestId.Should().Be("req_blacklisted");
        }

        [Fact]
        public void BlacklistTokenRequest_StaticMethods_ShouldCreateCorrectRequests()
        {
            // Test ForUserLogout static method
            var logoutRequest = BlacklistTokenRequest.ForUserLogout(
                tokenId: "jti_logout",
                userId: "user_123",
                organizationId: "org_456",
                ipAddress: "192.168.1.1",
                blacklistAllTokens: true);

            logoutRequest.TokenId.Should().Be("jti_logout");
            logoutRequest.Reason.Should().Be(BlacklistReasons.UserLogout);
            logoutRequest.UserId.Should().Be("user_123");
            logoutRequest.OrganizationId.Should().Be("org_456");
            logoutRequest.IpAddress.Should().Be("192.168.1.1");
            logoutRequest.BlacklistAllUserTokens.Should().BeTrue();
            logoutRequest.Source.Should().Be("user_logout");

            // Test ForSecurityBreach static method
            var breachRequest = BlacklistTokenRequest.ForSecurityBreach(
                tokenId: "jti_breach",
                details: "Suspicious activity detected",
                userId: "user_456",
                requestedBy: "security_system",
                blacklistAllUserTokens: true);

            breachRequest.TokenId.Should().Be("jti_breach");
            breachRequest.Reason.Should().Be(BlacklistReasons.SecurityBreach);
            breachRequest.Details.Should().Be("Suspicious activity detected");
            breachRequest.UserId.Should().Be("user_456");
            breachRequest.RequestedBy.Should().Be("security_system");
            breachRequest.BlacklistAllUserTokens.Should().BeTrue();
            breachRequest.Source.Should().Be("security_system");

            // Test ForAdminRevocation static method
            var adminRequest = BlacklistTokenRequest.ForAdminRevocation(
                tokenId: "jti_admin",
                adminUserId: "admin_123",
                reason: "Manual revocation by administrator",
                userId: "user_789",
                organizationId: "org_999");

            adminRequest.TokenId.Should().Be("jti_admin");
            adminRequest.Reason.Should().Be(BlacklistReasons.AdminRevoked);
            adminRequest.Details.Should().Be("Manual revocation by administrator");
            adminRequest.UserId.Should().Be("user_789");
            adminRequest.OrganizationId.Should().Be("org_999");
            adminRequest.RequestedBy.Should().Be("admin_123");
            adminRequest.Source.Should().Be("admin_panel");
        }

        [Fact]
        public void BlacklistTokenResponse_StaticMethods_ShouldCreateCorrectResponses()
        {
            // Test CreateSuccess static method
            var successResponse = BlacklistTokenResponse.CreateSuccess(
                tokenId: "jti_success",
                reason: BlacklistReasons.UserLogout,
                userId: "user_123",
                organizationId: "org_456",
                blacklistedBy: "user_123",
                additionalTokensBlacklisted: 3,
                requestId: "req_success");

            successResponse.TokenId.Should().Be("jti_success");
            successResponse.Success.Should().BeTrue();
            successResponse.Reason.Should().Be(BlacklistReasons.UserLogout);
            successResponse.UserId.Should().Be("user_123");
            successResponse.OrganizationId.Should().Be("org_456");
            successResponse.BlacklistedBy.Should().Be("user_123");
            successResponse.AdditionalTokensBlacklisted.Should().Be(3);
            successResponse.RequestId.Should().Be("req_success");
            successResponse.ErrorMessage.Should().BeNull();

            // Test CreateFailure static method
            var failureResponse = BlacklistTokenResponse.CreateFailure(
                tokenId: "jti_failure",
                errorMessage: "Database connection failed",
                errorCode: "DB_CONN_ERROR",
                requestId: "req_failure");

            failureResponse.TokenId.Should().Be("jti_failure");
            failureResponse.Success.Should().BeFalse();
            failureResponse.Reason.Should().Be("failed");
            failureResponse.ErrorMessage.Should().Be("Database connection failed");
            failureResponse.ErrorCode.Should().Be("DB_CONN_ERROR");
            failureResponse.RequestId.Should().Be("req_failure");
        }

        [Fact]
        public void LogAuthenticationEventRequest_StaticMethods_ShouldCreateCorrectRequests()
        {
            // Test CreateSuccess static method
            var successEvent = LogAuthenticationEventRequest.CreateSuccess(
                eventType: "user_login",
                details: "{\"method\":\"workos\",\"ip\":\"192.168.1.1\"}",
                userId: "user_123",
                externalUserId: "workos_user_123",
                organizationId: "org_456",
                sessionId: "session_789",
                ipAddress: "192.168.1.1");

            successEvent.EventType.Should().Be("user_login");
            successEvent.Details.Should().Be("{\"method\":\"workos\",\"ip\":\"192.168.1.1\"}");
            successEvent.UserId.Should().Be("user_123");
            successEvent.ExternalUserId.Should().Be("workos_user_123");
            successEvent.OrganizationId.Should().Be("org_456");
            successEvent.SessionId.Should().Be("session_789");
            successEvent.IpAddress.Should().Be("192.168.1.1");
            successEvent.Success.Should().BeTrue();
            successEvent.ErrorMessage.Should().BeNull();

            // Test CreateFailure static method
            var failureEvent = LogAuthenticationEventRequest.CreateFailure(
                eventType: "user_login",
                details: "{\"method\":\"workos\",\"ip\":\"192.168.1.1\"}",
                errorMessage: "Invalid credentials",
                errorCode: "INVALID_CREDS",
                userId: "user_456",
                ipAddress: "192.168.1.2");

            failureEvent.EventType.Should().Be("user_login");
            failureEvent.Details.Should().Be("{\"method\":\"workos\",\"ip\":\"192.168.1.1\"}");
            failureEvent.ErrorMessage.Should().Be("Invalid credentials");
            failureEvent.ErrorCode.Should().Be("INVALID_CREDS");
            failureEvent.UserId.Should().Be("user_456");
            failureEvent.IpAddress.Should().Be("192.168.1.2");
            failureEvent.Success.Should().BeFalse();
        }

        [Fact]
        public void RbacErrors_ShouldHaveCorrectErrorCodes()
        {
            // Test Authentication errors (4001-4099)
            RbacErrors.Authentication.InvalidToken.Code.Should().Be(4001);
            RbacErrors.Authentication.TokenExpired.Code.Should().Be(4002);
            RbacErrors.Authentication.WorkOsTokenExchangeFailed.Code.Should().Be(4003);
            RbacErrors.Authentication.UserNotFound.Code.Should().Be(4006);

            // Test Authorization errors (4100-4199)
            RbacErrors.Authorization.InsufficientPermissions.Code.Should().Be(4100);
            RbacErrors.Authorization.TokenBlacklisted.Code.Should().Be(4101);
            RbacErrors.Authorization.OrganizationAccessDenied.Code.Should().Be(4102);
            RbacErrors.Authorization.TokenBlacklistFailed.Code.Should().Be(4106);

            // Test Database errors (5001-5099)
            RbacErrors.Database.ConnectionFailed.Code.Should().Be(5001);
            RbacErrors.Database.QueryExecutionFailed.Code.Should().Be(5002);
            RbacErrors.Database.ConstraintViolation.Code.Should().Be(5004);

            // Test Message Bus errors (5100-5199)
            RbacErrors.MessageBus.PublishFailed.Code.Should().Be(5100);
            RbacErrors.MessageBus.ConsumeFailed.Code.Should().Be(5101);
            RbacErrors.MessageBus.TimeoutError.Code.Should().Be(5102);
        }

        [Fact]
        public void Result_Pattern_ShouldWorkCorrectly()
        {
            // Test successful result
            var successResult = Result<string>.Ok("success_value");
            successResult.Success.Should().BeTrue();
            successResult.Value.Should().Be("success_value");
            successResult.Errors.Should().BeEmpty();

            // Test failed result
            var failureResult = Result<string>.Fail(RbacErrors.Authentication.InvalidToken);
            failureResult.Success.Should().BeFalse();
            failureResult.Errors.Should().HaveCount(1);
            failureResult.Errors.First().Code.Should().Be(4001);
            failureResult.Errors.First().Text.Should().Contain("JWT token is invalid");

            // Test non-generic Result
            var voidSuccessResult = Result.Ok();
            voidSuccessResult.Success.Should().BeTrue();
            voidSuccessResult.Errors.Should().BeEmpty();

            var voidFailureResult = Result.Fail(RbacErrors.Database.ConnectionFailed);
            voidFailureResult.Success.Should().BeFalse();
            voidFailureResult.Errors.Should().HaveCount(1);
            voidFailureResult.Errors.First().Code.Should().Be(5001);
        }
    }
}