namespace Nyssa.Wasm.Features.Authentication
{
    public class OidcConfiguration
    {
        public string Authority { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string PostLogoutRedirectUri { get; set; } = string.Empty;
        public string ResponseType { get; set; } = "code";
        public string Scope { get; set; } = "openid profile email";
        public string ApiKey { get; set; } = string.Empty;
    }
} 