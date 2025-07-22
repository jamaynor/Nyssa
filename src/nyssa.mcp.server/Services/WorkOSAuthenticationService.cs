using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using Nyssa.Mcp.Server.Models;

namespace Nyssa.Mcp.Server.Services
{
    public class WorkOSAuthenticationService
    {
        private readonly OidcConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<WorkOSAuthenticationService> _logger;

        public WorkOSAuthenticationService(
            IOptions<OidcConfiguration> options,
            HttpClient httpClient,
            ILogger<WorkOSAuthenticationService> logger)
        {
            _config = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<AuthenticationResult> ExchangeCodeForTokenAsync(string code)
        {
            try
            {
                _logger.LogInformation("Exchanging authorization code for token");
                
                // Exchange authorization code for access token
                var tokenResponse = await ExchangeCodeForAccessTokenAsync(code);
                
                // Get user profile from WorkOS
                var userProfile = await GetUserProfileAsync(tokenResponse.AccessToken);
                
                // Calculate token expiration (default to 1 hour if not provided)
                var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600);
                
                return new AuthenticationResult
                {
                    User = userProfile,
                    AccessToken = tokenResponse.AccessToken,
                    IdToken = tokenResponse.IdToken,
                    ExpiresAt = expiresAt,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token exchange failed: {Message}", ex.Message);
                return new AuthenticationResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<TokenResponse> ExchangeCodeForAccessTokenAsync(string code)
        {
            var tokenRequest = new
            {
                client_id = _config.ClientId,
                client_secret = _config.ApiKey,
                code = code,
                grant_type = "authorization_code"
            };

            var json = JsonSerializer.Serialize(tokenRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("https://api.workos.com/sso/token", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {ResponseContent}", responseContent);
                throw new Exception($"Token exchange failed with status {response.StatusCode}");
            }
            
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            
            return tokenResponse ?? throw new Exception("Failed to parse token response");
        }

        private async Task<UserProfile> GetUserProfileAsync(string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.workos.com/user_management/me");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("User profile fetch failed: {ResponseContent}", responseContent);
                throw new Exception($"Failed to get user profile with status {response.StatusCode}");
            }
            
            var workOSUser = JsonSerializer.Deserialize<WorkOSUser>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            
            if (workOSUser == null)
                throw new Exception("Failed to parse user profile response");
            
            return new UserProfile
            {
                Id = workOSUser.Id,
                Email = workOSUser.Email,
                FirstName = workOSUser.FirstName ?? "",
                LastName = workOSUser.LastName ?? "",
                ProfilePictureUrl = workOSUser.ProfilePictureUrl ?? "",
                CreatedAt = workOSUser.CreatedAt,
                UpdatedAt = workOSUser.UpdatedAt
            };
        }

        public string BuildAuthorizationUrl()
        {
            var state = Guid.NewGuid().ToString("N")[..16];
            
            var queryParams = new Dictionary<string, string>
            {
                {"response_type", "code"},
                {"provider", "authkit"},
                {"client_id", _config.ClientId},
                {"redirect_uri", _config.RedirectUri},
                {"state", state}
            };

            var queryString = string.Join("&", 
                queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            
            return $"https://api.workos.com/user_management/authorize?{queryString}";
        }
    }
}