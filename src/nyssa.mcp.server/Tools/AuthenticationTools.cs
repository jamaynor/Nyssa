using MCPSharp;
using Nyssa.Mcp.Server.Services;
using System.Text.Json;

namespace Nyssa.Mcp.Server.Tools
{
    public class AuthenticationTools
    {
        private static WorkOSAuthenticationService? _authService;

        public AuthenticationTools()
        {
        }

        public static void Initialize(WorkOSAuthenticationService authService)
        {
            _authService = authService;
        }

        [McpTool("exchange_code", "Exchange authorization code for access token and user profile")]
        public async Task<string> ExchangeCodeAsync([McpParameter(true, "Authorization code from WorkOS")] string code)
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                var result = await _authService.ExchangeCodeForTokenAsync(code);
                return JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        [McpTool("get_auth_url", "Get WorkOS authorization URL for user login")]
        public string GetAuthUrl()
        {
            try
            {
                if (_authService == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Authentication service not initialized" });

                var authUrl = _authService.BuildAuthorizationUrl();
                return JsonSerializer.Serialize(new { authUrl }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }
    }
}