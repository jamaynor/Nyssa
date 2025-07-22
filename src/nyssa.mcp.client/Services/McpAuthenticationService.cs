using Nyssa.Mcp.Client.Models;
using System.Text.Json;
using System.Net.Http.Json;

namespace Nyssa.Mcp.Client.Services
{
    public class McpAuthenticationService
    {
        private readonly HttpClient _httpClient;

        public McpAuthenticationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetAuthorizationUrlAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://localhost:7001/api/auth/url");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API call failed: {response.StatusCode} - {errorContent}");
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AuthUrlResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                return result?.AuthUrl ?? throw new Exception("Failed to get authorization URL");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get authorization URL: {ex.Message}", ex);
            }
        }

        public async Task<AuthenticationResult> ExchangeCodeForTokenAsync(string code)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("https://localhost:7001/api/auth/exchange", code);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API call failed: {response.StatusCode} - {errorContent}");
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                var authResult = JsonSerializer.Deserialize<AuthenticationResult>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                return authResult ?? throw new Exception("Failed to parse authentication result");
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private class AuthUrlResponse
        {
            public string AuthUrl { get; set; } = string.Empty;
        }
    }
}