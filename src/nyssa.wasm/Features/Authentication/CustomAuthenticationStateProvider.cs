using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Nyssa.Wasm.Features.Authentication
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationStateService _authStateService;

        public CustomAuthenticationStateProvider(AuthenticationStateService authStateService)
        {
            _authStateService = authStateService;
            _authStateService.AuthStateChanged += OnAuthStateChanged;
        }

        public override async Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> GetAuthenticationStateAsync()
        {
            var isAuthenticated = await _authStateService.IsAuthenticatedAsync();
            
            if (!isAuthenticated)
            {
                return new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var authState = _authStateService.GetAuthState();
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, authState.User?.Id ?? ""),
                new(ClaimTypes.Email, authState.User?.Email ?? ""),
                new(ClaimTypes.GivenName, authState.User?.FirstName ?? ""),
                new(ClaimTypes.Surname, authState.User?.LastName ?? ""),
                new(ClaimTypes.Name, authState.User?.FullName ?? "")
            };

            var identity = new ClaimsIdentity(claims, "oidc");
            var user = new ClaimsPrincipal(identity);

            return new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(user);
        }

        public async Task MarkUserAsAuthenticated()
        {
            var authState = await GetAuthenticationStateAsync();
            NotifyAuthenticationStateChanged(Task.FromResult(authState));
        }

        public void MarkUserAsLoggedOut()
        {
            var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(anonymousUser)));
        }

        private async void OnAuthStateChanged()
        {
            var authState = await GetAuthenticationStateAsync();
            NotifyAuthenticationStateChanged(Task.FromResult(authState));
        }

        public void Dispose()
        {
            _authStateService.AuthStateChanged -= OnAuthStateChanged;
        }
    }
}