namespace Nyssa.Wasm.Features.Authentication
{
    public class AuthenticationState
    {
        public bool IsAuthenticated { get; set; }
        public UserProfile? User { get; set; }
        public string? AccessToken { get; set; }
        public DateTime? TokenExpiresAt { get; set; }
        
        public bool IsTokenExpired => TokenExpiresAt.HasValue && TokenExpiresAt.Value <= DateTime.UtcNow;
    }
}