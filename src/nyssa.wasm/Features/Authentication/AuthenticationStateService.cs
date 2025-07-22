using Microsoft.JSInterop;
using System.Text.Json;

namespace Nyssa.Wasm.Features.Authentication
{
    public class AuthenticationStateService
    {
        private readonly IJSRuntime _jsRuntime;
        private AuthenticationState _authState = new();
        public event Action? AuthStateChanged;

        public AuthenticationStateService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public AuthenticationState GetAuthState() => _authState;

        public async Task<bool> IsAuthenticatedAsync()
        {
            if (!_authState.IsAuthenticated)
            {
                await LoadAuthStateFromStorageAsync();
            }
            return _authState.IsAuthenticated && !_authState.IsTokenExpired;
        }

        public async Task SetAuthenticatedAsync(UserProfile user, string accessToken, DateTime expiresAt)
        {
            _authState = new AuthenticationState
            {
                IsAuthenticated = true,
                User = user,
                AccessToken = accessToken,
                TokenExpiresAt = expiresAt
            };

            await SaveAuthStateToStorageAsync();
            AuthStateChanged?.Invoke();
        }

        public async Task LogoutAsync()
        {
            _authState = new AuthenticationState();
            await ClearAuthStateFromStorageAsync();
            AuthStateChanged?.Invoke();
        }

        private async Task SaveAuthStateToStorageAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_authState);
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "auth_state", json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving auth state: {ex.Message}");
            }
        }

        private async Task LoadAuthStateFromStorageAsync()
        {
            try
            {
                var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "auth_state");
                if (!string.IsNullOrEmpty(json))
                {
                    var authState = JsonSerializer.Deserialize<AuthenticationState>(json);
                    if (authState != null && !authState.IsTokenExpired)
                    {
                        _authState = authState;
                    }
                    else if (authState?.IsTokenExpired == true)
                    {
                        await ClearAuthStateFromStorageAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading auth state: {ex.Message}");
                await ClearAuthStateFromStorageAsync();
            }
        }

        private async Task ClearAuthStateFromStorageAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "auth_state");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing auth state: {ex.Message}");
            }
        }

        public async Task InitializeAsync()
        {
            await LoadAuthStateFromStorageAsync();
        }
    }
}