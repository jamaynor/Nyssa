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
                _logger.LogInformation("=== WORKOS TOKEN EXCHANGE START ===");
                _logger.LogInformation("Exchanging authorization code for token");
                _logger.LogInformation("Authorization code: {Code}", code);
                _logger.LogInformation("Client ID: {ClientId}", _config.ClientId);
                _logger.LogInformation("Has API Key: {HasApiKey}", !string.IsNullOrEmpty(_config.ApiKey));
                
                // Exchange authorization code for access token
                _logger.LogInformation("Step 1: Exchanging code for access token");
                var tokenResponse = await ExchangeCodeForAccessTokenAsync(code);
                
                _logger.LogInformation("Token exchange successful - Access token length: {TokenLength}", tokenResponse.AccessToken?.Length ?? 0);
                
                // Extract user profile from token response
                _logger.LogInformation("Step 2: Extracting user profile from token response");
                if (tokenResponse.User == null)
                {
                    throw new Exception("User profile not included in token response");
                }
                
                var userProfile = new UserProfile
                {
                    Id = tokenResponse.User.Id,
                    Email = tokenResponse.User.Email,
                    FirstName = tokenResponse.User.FirstName ?? "",
                    LastName = tokenResponse.User.LastName ?? "",
                    ProfilePictureUrl = tokenResponse.User.ProfilePictureUrl ?? "",
                    CreatedAt = tokenResponse.User.CreatedAt,
                    UpdatedAt = tokenResponse.User.UpdatedAt
                };
                
                _logger.LogInformation("User profile extracted - ID: {UserId}, Email: {Email}", userProfile.Id, userProfile.Email);
                
                // Calculate token expiration (default to 1 hour if not provided)
                var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600);
                
                _logger.LogInformation("Token expires at: {ExpiresAt}", expiresAt);
                _logger.LogInformation("=== WORKOS TOKEN EXCHANGE SUCCESS ===");
                
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
                _logger.LogError(ex, "=== WORKOS TOKEN EXCHANGE FAILED ===");
                _logger.LogError(ex, "Token exchange failed: {Message}", ex.Message);
                _logger.LogError("Exception type: {ExceptionType}", ex.GetType().Name);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                return new AuthenticationResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<TokenResponse> ExchangeCodeForAccessTokenAsync(string code)
        {
            _logger.LogInformation("--- TOKEN REQUEST START ---");
            
            var tokenRequest = new
            {
                client_id = _config.ClientId,
                client_secret = _config.ApiKey,
                code = code,
                grant_type = "authorization_code"
            };

            var json = JsonSerializer.Serialize(tokenRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Token request payload: {Payload}", json);
            _logger.LogInformation("Making POST request to: https://api.workos.com/user_management/authenticate");
            
            var response = await _httpClient.PostAsync("https://api.workos.com/user_management/authenticate", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            _logger.LogInformation("Token exchange response status: {StatusCode}", response.StatusCode);
            _logger.LogInformation("Token exchange response body: {ResponseBody}", responseContent);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed with status {StatusCode}: {ResponseContent}", response.StatusCode, responseContent);
                throw new Exception($"Token exchange failed with status {response.StatusCode}: {responseContent}");
            }
            
            _logger.LogInformation("Deserializing token response");
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            
            if (tokenResponse == null)
            {
                _logger.LogError("Failed to deserialize token response");
                throw new Exception("Failed to parse token response");
            }
            
            _logger.LogInformation("--- TOKEN REQUEST SUCCESS ---");
            return tokenResponse;
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
            _logger.LogInformation("--- AUTHORIZATION URL BUILD START ---");
            
            var state = Guid.NewGuid().ToString("N")[..16];
            
            _logger.LogInformation("Generated state: {State}", state);
            _logger.LogInformation("Client ID: {ClientId}", _config.ClientId);
            _logger.LogInformation("Redirect URI: {RedirectUri}", _config.RedirectUri);
            
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
            
            var authUrl = $"https://api.workos.com/user_management/authorize?{queryString}";
            
            _logger.LogInformation("Built authorization URL: {AuthUrl}", authUrl);
            _logger.LogInformation("--- AUTHORIZATION URL BUILD SUCCESS ---");
            
            return authUrl;
        }
    }
}