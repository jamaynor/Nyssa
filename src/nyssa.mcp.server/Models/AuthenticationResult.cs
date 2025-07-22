namespace Nyssa.Mcp.Server.Models
{
    public class AuthenticationResult
    {
        public UserProfile? User { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string? IdToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }
}