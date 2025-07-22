using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Nyssa.Mcp.Client.Services;
using Nyssa.Mcp.Client.Models;

namespace Nyssa.Wasm.Features.Authentication
{
    public class OidcAuthenticationService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly AuthenticationStateService _authStateService;
        private readonly McpAuthenticationService _mcpAuthService;
        private readonly CustomAuthenticationStateProvider _authStateProvider;

        public OidcAuthenticationService(
            IJSRuntime jsRuntime,
            AuthenticationStateService authStateService,
            McpAuthenticationService mcpAuthService,
            CustomAuthenticationStateProvider authStateProvider)
        {
            _jsRuntime = jsRuntime;
            _authStateService = authStateService;
            _mcpAuthService = mcpAuthService;
            _authStateProvider = authStateProvider;
        }

        public async Task LoginAsync()
        {
            try
            {
                var authorizationUrl = await _mcpAuthService.GetAuthorizationUrlAsync();
                Console.WriteLine($"Redirecting to: {authorizationUrl}");
                await _jsRuntime.InvokeVoidAsync("open", authorizationUrl, "_self");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
                throw;
            }
        }

        public async Task ExchangeCodeForTokenAsync(string code)
        {
            try
            {
                Console.WriteLine($"Exchanging authorization code: {code}");
                
                // Exchange authorization code for access token via MCP
                var result = await _mcpAuthService.ExchangeCodeForTokenAsync(code);
                
                if (!result.IsSuccess)
                {
                    throw new Exception(result.ErrorMessage ?? "Token exchange failed");
                }

                if (result.User == null)
                {
                    throw new Exception("User profile is null in authentication result");
                }
                
                // Convert MCP client models to WASM models
                var userProfile = new UserProfile
                {
                    Id = result.User.Id,
                    Email = result.User.Email,
                    FirstName = result.User.FirstName,
                    LastName = result.User.LastName,
                    ProfilePictureUrl = result.User.ProfilePictureUrl,
                    CreatedAt = result.User.CreatedAt,
                    UpdatedAt = result.User.UpdatedAt
                };
                
                // Update authentication state
                await _authStateService.SetAuthenticatedAsync(userProfile, result.AccessToken, result.ExpiresAt);
                
                // Notify the authentication state provider
                await _authStateProvider.MarkUserAsAuthenticated();
                
                Console.WriteLine($"Successfully authenticated user: {userProfile.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token exchange failed: {ex.Message}");
                throw;
            }
        }

        public async Task LogoutAsync()
        {
            await _authStateService.LogoutAsync();
            _authStateProvider.MarkUserAsLoggedOut();
        }
    }
}