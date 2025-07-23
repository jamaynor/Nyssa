namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Represents an error message with structured information for different audiences
    /// </summary>
    public record ErrorMessage
    {
        /// <summary>
        /// HTTP-style error code for categorization and processing
        /// 4xxx for client errors, 5xxx for server errors
        /// </summary>
        public int Code { get; init; }

        /// <summary>
        /// Technical error description for developers and logging
        /// </summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>
        /// User-facing error message for UI display
        /// </summary>
        public string UserFriendlyText { get; init; } = string.Empty;

        public ErrorMessage(int code, string text, string userFriendlyText)
        {
            Code = code;
            Text = text;
            UserFriendlyText = userFriendlyText;
        }

        /// <summary>
        /// Creates an authentication error (4001-4099)
        /// </summary>
        public static ErrorMessage Authentication(int code, string text, string userFriendlyText = "Authentication failed")
        {
            if (code < 4001 || code > 4099)
                throw new ArgumentException("Authentication error codes must be between 4001-4099", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        /// <summary>
        /// Creates an authorization error (4100-4199)
        /// </summary>
        public static ErrorMessage Authorization(int code, string text, string userFriendlyText = "Access denied")
        {
            if (code < 4100 || code > 4199)
                throw new ArgumentException("Authorization error codes must be between 4100-4199", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        /// <summary>
        /// Creates an RBAC validation error (4200-4299)
        /// </summary>
        public static ErrorMessage RbacValidation(int code, string text, string userFriendlyText = "Validation failed")
        {
            if (code < 4200 || code > 4299)
                throw new ArgumentException("RBAC validation error codes must be between 4200-4299", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        /// <summary>
        /// Creates a database error (5001-5099)
        /// </summary>
        public static ErrorMessage Database(int code, string text, string userFriendlyText = "A system error occurred")
        {
            if (code < 5001 || code > 5099)
                throw new ArgumentException("Database error codes must be between 5001-5099", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        /// <summary>
        /// Creates a message bus error (5100-5199)
        /// </summary>
        public static ErrorMessage MessageBus(int code, string text, string userFriendlyText = "A system error occurred")
        {
            if (code < 5100 || code > 5199)
                throw new ArgumentException("Message bus error codes must be between 5100-5199", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        /// <summary>
        /// Creates an external service error (5200-5299)
        /// </summary>
        public static ErrorMessage ExternalService(int code, string text, string userFriendlyText = "A system error occurred")
        {
            if (code < 5200 || code > 5299)
                throw new ArgumentException("External service error codes must be between 5200-5299", nameof(code));
            
            return new ErrorMessage(code, text, userFriendlyText);
        }

        public override string ToString() => $"[{Code}] {Text}";
    }
}