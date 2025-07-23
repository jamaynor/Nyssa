namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Legacy authentication result class. Consider migrating to Result<AuthenticationData> pattern.
    /// </summary>
    [Obsolete("Use Result<AuthenticationData> instead for new implementations")]
    public class AuthenticationResult
    {
        public UserProfile? User { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string? IdToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Modern authentication data structure to be used with Result<T> pattern
    /// </summary>
    public record AuthenticationData
    {
        public UserProfile User { get; init; } = new();
        public string AccessToken { get; init; } = string.Empty;
        public string? IdToken { get; init; }
        public DateTime ExpiresAt { get; init; }

        public AuthenticationData() { }

        public AuthenticationData(UserProfile user, string accessToken, DateTime expiresAt, string? idToken = null)
        {
            User = user;
            AccessToken = accessToken;
            ExpiresAt = expiresAt;
            IdToken = idToken;
        }
    }
}