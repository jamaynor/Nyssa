using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nyssa.Mcp.Server.Authorization;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Services;
using System.Reflection;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Tests
{
    /// <summary>
    /// Tests for the MCP Authorization Middleware with realistic MCP tool scenarios
    /// </summary>
    public class McpAuthorizationMiddlewareTests
    {
        private readonly McpAuthorizationMiddleware _middleware;
        private readonly Mock<IMcpAuthorizationService> _mockAuthService;
        private readonly Mock<ILogger<McpAuthorizationMiddleware>> _mockLogger;

        public McpAuthorizationMiddlewareTests()
        {
            _mockAuthService = new Mock<IMcpAuthorizationService>();
            _mockLogger = new Mock<ILogger<McpAuthorizationMiddleware>>();
            _middleware = new McpAuthorizationMiddleware(_mockAuthService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task Middleware_Should_Allow_Authorized_Tool_Execution()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AuthorizedMethod))!;
            var parameters = new object[] { "Bearer valid-token", "test-param" };
            var authToken = "Bearer valid-token";

            var authContext = new McpAuthorizationContext
            {
                IsAuthenticated = true,
                User = new McpUser { Id = "user123", Email = "test@example.com", Name = "Test User" },
                Organization = new McpOrganization { Id = "org123", Name = "Test Org" },
                Permissions = new List<string> { "users:read", "users:write" }
            };

            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, authToken, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Ok(authContext));

            // Act
            var result = await _middleware.InterceptToolCallAsync(method, testTool, parameters, authToken);

            // Assert
            result.Should().NotBeNull();
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("True");
            
            // Verify authorization was called
            _mockAuthService.Verify(x => x.AuthorizeToolAsync(method, authToken, null), Times.Once);
        }

        [Fact]
        public async Task Middleware_Should_Block_Unauthorized_Tool_Execution()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.RestrictedMethod))!;
            var parameters = new object[] { "Bearer invalid-token" };
            var authToken = "Bearer invalid-token";

            var authError = RbacErrors.Authorization.InsufficientPermissions;
            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, authToken, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Fail(authError));

            // Act
            var result = await _middleware.InterceptToolCallAsync(method, testTool, parameters, authToken);

            // Assert
            result.Should().NotBeNull();
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("False");
            response.Should().ContainKey("authorized");
            response["authorized"].ToString().Should().Be("False");
            response.Should().ContainKey("error_code");
            response["error_code"].ToString().Should().Be(authError.Code.ToString());

            // Verify authorization was called but tool method was not executed
            _mockAuthService.Verify(x => x.AuthorizeToolAsync(method, authToken, null), Times.Once);
        }

        [Fact]
        public async Task Middleware_Should_Allow_Anonymous_Tools()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AnonymousMethod))!;
            var parameters = new object[] { "public-param" };

            var authContext = new McpAuthorizationContext { IsAuthenticated = false };
            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, null, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Ok(authContext));

            // Act
            var result = await _middleware.InterceptToolCallAsync(method, testTool, parameters, null);

            // Assert
            result.Should().NotBeNull();
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("True");
            response.Should().ContainKey("message");
            response["message"].ToString().Should().Be("Anonymous access successful");
        }

        [Fact]
        public async Task Middleware_Should_Handle_Tool_Execution_Errors()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.ErrorMethod))!;
            var parameters = new object[] { "Bearer valid-token" };
            var authToken = "Bearer valid-token";

            var authContext = new McpAuthorizationContext
            {
                IsAuthenticated = true,
                User = new McpUser { Id = "user123", Email = "test@example.com" },
                Permissions = new List<string> { "users:read" }
            };

            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, authToken, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Ok(authContext));

            // Act
            var result = await _middleware.InterceptToolCallAsync(method, testTool, parameters, authToken);

            // Assert
            result.Should().NotBeNull();
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("False");
            response.Should().ContainKey("error_type");
            response["error_type"].ToString().Should().Be("execution_error");
            response.Should().ContainKey("details");
            response["details"].ToString().Should().Contain("Test error");
        }

        [Fact]
        public async Task Middleware_Should_Handle_Async_Tool_Methods()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AsyncMethod))!;
            var parameters = new object[] { "Bearer valid-token", "async-param" };
            var authToken = "Bearer valid-token";

            var authContext = new McpAuthorizationContext
            {
                IsAuthenticated = true,
                User = new McpUser { Id = "user123", Email = "test@example.com" },
                Permissions = new List<string> { "projects:read" }
            };

            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, authToken, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Ok(authContext));

            // Act
            var result = await _middleware.InterceptToolCallAsync(method, testTool, parameters, authToken);

            // Assert
            result.Should().NotBeNull();
            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
            response.Should().ContainKey("success");
            response["success"].ToString().Should().Be("True");
            response.Should().ContainKey("message");
            response["message"].ToString().Should().Be("Async operation completed");
            response.Should().ContainKey("parameter");
            response["parameter"].ToString().Should().Be("async-param");
        }

        [Theory]
        [InlineData("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test.signature")]
        [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test.signature")]
        public void ExtractAuthorizationToken_Should_Detect_JWT_Tokens(string token)
        {
            // Arrange
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AuthorizedMethod))!;
            var parameters = new object[] { token, "other-param" };

            // Act
            var extractedToken = McpAuthorizationMiddleware.ExtractAuthorizationToken(parameters, method);

            // Assert
            extractedToken.Should().Be(token);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-token")]
        [InlineData("short")]
        [InlineData("has spaces in it")]
        public void ExtractAuthorizationToken_Should_Reject_Invalid_Tokens(string token)
        {
            // Arrange
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AuthorizedMethod))!;
            var parameters = new object[] { token, "other-param" };

            // Act
            var extractedToken = McpAuthorizationMiddleware.ExtractAuthorizationToken(parameters, method);

            // Assert
            extractedToken.Should().BeNull();
        }

        [Fact]
        public void CreateAuthorizedToolDelegate_Should_Create_Working_Delegate()
        {
            // Arrange
            var testTool = new TestMcpTool();
            var method = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AuthorizedMethod))!;
            var authToken = "Bearer test-token";

            var authContext = new McpAuthorizationContext
            {
                IsAuthenticated = true,
                User = new McpUser { Id = "user123" },
                Permissions = new List<string> { "users:read" }
            };

            _mockAuthService.Setup(x => x.AuthorizeToolAsync(method, authToken, null))
                .ReturnsAsync(Result<McpAuthorizationContext>.Ok(authContext));

            // Act
            var toolDelegate = _middleware.CreateAuthorizedToolDelegate(
                method, 
                testTool, 
                () => authToken);

            // Assert
            toolDelegate.Should().NotBeNull();

            // Execute the delegate
            var parameters = new object[] { authToken, "test-param" };
            var task = toolDelegate(parameters);
            task.Should().NotBeNull();
        }

        [Fact]
        public void Authorization_Extensions_Should_Work_Correctly()
        {
            // Test RequiresAuthentication
            var authMethod = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AuthorizedMethod))!;
            authMethod.RequiresAuthentication().Should().BeTrue();

            var anonymousMethod = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.AnonymousMethod))!;
            anonymousMethod.RequiresAuthentication().Should().BeFalse();

            // Test AllowsAnonymousAccess
            anonymousMethod.AllowsAnonymousAccess().Should().BeTrue();
            authMethod.AllowsAnonymousAccess().Should().BeFalse();

            // Test GetAuthorizationSummary
            var restrictedMethod = typeof(TestMcpTool).GetMethod(nameof(TestMcpTool.RestrictedMethod))!;
            var summary = restrictedMethod.GetAuthorizationSummary();
            summary.Should().Contain("users:write");
            summary.Should().Contain("admin:delete");

            var anonymousSummary = anonymousMethod.GetAuthorizationSummary();
            anonymousSummary.Should().Be("Anonymous access allowed");
        }

        // Test MCP tool class with various authorization scenarios
        private class TestMcpTool
        {
            [McpRequirePermission("users:read")]
            public string AuthorizedMethod(string authToken, string parameter)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Authorized method executed", 
                    parameter 
                });
            }

            [McpRequirePermission("users:write")]
            [McpRequirePermission("admin:delete")]
            public string RestrictedMethod(string authToken)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Restricted method executed" 
                });
            }

            [McpAllowAnonymous]
            public string AnonymousMethod(string parameter)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Anonymous access successful", 
                    parameter 
                });
            }

            [McpRequirePermission("users:read")]
            public string ErrorMethod(string authToken)
            {
                throw new InvalidOperationException("Test error from tool method");
            }

            [McpRequirePermission("projects:read")]
            public async Task<string> AsyncMethod(string authToken, string parameter)
            {
                await Task.Delay(10); // Simulate async work
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Async operation completed", 
                    parameter 
                });
            }

            [McpRequireRole("Admin")]
            public string AdminMethod(string authToken)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Admin method executed" 
                });
            }

            [McpRequireOrganization("specific-org-id")]
            public string OrganizationSpecificMethod(string authToken)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = true, 
                    message = "Organization-specific method executed" 
                });
            }
        }
    }
}