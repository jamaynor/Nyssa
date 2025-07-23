namespace Nyssa.Mcp.Server.Models.Messages.Authentication
{
    /// <summary>
    /// Authentication event types for audit logging
    /// </summary>
    public static class AuthEventTypes
    {
        public const string UserLogin = "user_login";
        public const string UserLogout = "user_logout";
        public const string UserCreated = "user_created";
        public const string UserResolved = "user_resolved";
        public const string TokenGenerated = "token_generated";
        public const string TokenValidated = "token_validated";
        public const string TokenRevoked = "token_revoked";
        public const string PermissionGranted = "permission_granted";
        public const string PermissionDenied = "permission_denied";
        public const string OrganizationSwitch = "organization_switch";
    }

    /// <summary>
    /// Authentication event categories for audit logging
    /// </summary>
    public static class AuthEventCategories
    {
        public const string Authentication = "authentication";
        public const string Authorization = "authorization";
        public const string UserManagement = "user_management";
        public const string TokenManagement = "token_management";
        public const string PermissionCheck = "permission_check";
    }

    /// <summary>
    /// Authentication event sources
    /// </summary>
    public static class AuthEventSources
    {
        public const string McpServer = "mcp_server";
        public const string BlazorWasm = "blazor_wasm";
        public const string WorkOsIntegration = "workos_integration";
        public const string RbacDatabase = "rbac_database";
        public const string MassTransit = "mass_transit";
    }

    /// <summary>
    /// Request to log an authentication event for audit purposes
    /// </summary>
    public record LogAuthenticationEventRequest
    {
        /// <summary>
        /// User ID involved in the event (if applicable)
        /// </summary>
        public string? UserId { get; init; }

        /// <summary>
        /// External user ID (WorkOS ID) involved in the event
        /// </summary>
        public string? ExternalUserId { get; init; }

        /// <summary>
        /// Event type (see AuthEventTypes constants)
        /// </summary>
        public string EventType { get; init; } = string.Empty;

        /// <summary>
        /// Event category (see AuthEventCategories constants)
        /// </summary>
        public string Category { get; init; } = AuthEventCategories.Authentication;

        /// <summary>
        /// Source system that generated the event (see AuthEventSources constants)
        /// </summary>
        public string Source { get; init; } = AuthEventSources.McpServer;

        /// <summary>
        /// Detailed event information (JSON format)
        /// </summary>
        public string Details { get; init; } = string.Empty;

        /// <summary>
        /// Organization ID involved in the event (if applicable)
        /// </summary>
        public string? OrganizationId { get; init; }

        /// <summary>
        /// Session ID or request ID for correlation
        /// </summary>
        public string? SessionId { get; init; }

        /// <summary>
        /// IP address of the client (if available)
        /// </summary>
        public string? IpAddress { get; init; }

        /// <summary>
        /// User agent string (if available)
        /// </summary>
        public string? UserAgent { get; init; }

        /// <summary>
        /// Whether the event represents a successful action
        /// </summary>
        public bool Success { get; init; } = true;

        /// <summary>
        /// Error message if the event represents a failure
        /// </summary>
        public string? ErrorMessage { get; init; }

        /// <summary>
        /// Error code if the event represents a failure
        /// </summary>
        public string? ErrorCode { get; init; }

        /// <summary>
        /// Timestamp when the event occurred
        /// </summary>
        public DateTime EventAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique event ID for deduplication
        /// </summary>
        public string EventId { get; init; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public LogAuthenticationEventRequest() { }

        public LogAuthenticationEventRequest(
            string eventType,
            string details,
            string? userId = null,
            string? externalUserId = null,
            string category = AuthEventCategories.Authentication,
            string source = AuthEventSources.McpServer,
            string? organizationId = null,
            string? sessionId = null,
            string? ipAddress = null,
            bool success = true,
            string? errorMessage = null,
            string? errorCode = null)
        {
            EventType = eventType;
            Details = details;
            UserId = userId;
            ExternalUserId = externalUserId;
            Category = category;
            Source = source;
            OrganizationId = organizationId;
            SessionId = sessionId;
            IpAddress = ipAddress;
            Success = success;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Creates a successful authentication event
        /// </summary>
        public static LogAuthenticationEventRequest CreateSuccess(
            string eventType,
            string details,
            string? userId = null,
            string? externalUserId = null,
            string? organizationId = null,
            string? sessionId = null,
            string? ipAddress = null)
        {
            return new LogAuthenticationEventRequest(
                eventType, details, userId, externalUserId,
                organizationId: organizationId,
                sessionId: sessionId,
                ipAddress: ipAddress,
                success: true);
        }

        /// <summary>
        /// Creates a failed authentication event
        /// </summary>
        public static LogAuthenticationEventRequest CreateFailure(
            string eventType,
            string details,
            string errorMessage,
            string? errorCode = null,
            string? userId = null,
            string? externalUserId = null,
            string? organizationId = null,
            string? sessionId = null,
            string? ipAddress = null)
        {
            return new LogAuthenticationEventRequest(
                eventType, details, userId, externalUserId,
                organizationId: organizationId,
                sessionId: sessionId,
                ipAddress: ipAddress,
                success: false,
                errorMessage: errorMessage,
                errorCode: errorCode);
        }
    }
}