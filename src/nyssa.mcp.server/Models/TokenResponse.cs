namespace Nyssa.Mcp.Server.Models
{
    public class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = string.Empty;
        public int? ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public WorkOSUser? User { get; set; }
        public string? AuthenticationMethod { get; set; }
    }
}