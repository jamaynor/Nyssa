namespace Nyssa.Mcp.Server.Models.Messages.Authorization
{
    /// <summary>
    /// Permission validation context for fine-grained access control
    /// </summary>
    public record PermissionContext
    {
        /// <summary>
        /// Resource ID being accessed (e.g., specific user ID, project ID)
        /// </summary>
        public string? ResourceId { get; init; }

        /// <summary>
        /// Resource attributes for attribute-based access control
        /// </summary>
        public Dictionary<string, string> ResourceAttributes { get; init; } = new();

        /// <summary>
        /// Request attributes (e.g., IP address, time of day)
        /// </summary>
        public Dictionary<string, string> RequestAttributes { get; init; } = new();

        /// <summary>
        /// Parent resource context for hierarchical resources
        /// </summary>
        public string? ParentResourceId { get; init; }

        /// <summary>
        /// Whether this is a cross-organization request
        /// </summary>
        public bool IsCrossOrganization { get; init; }
    }

    /// <summary>
    /// Request to validate if a user has permission to perform an action
    /// </summary>
    public record ValidatePermissionRequest
    {
        /// <summary>
        /// User ID requesting permission
        /// </summary>
        public string UserId { get; init; } = string.Empty;

        /// <summary>
        /// Organization context for the permission check
        /// </summary>
        public string OrganizationId { get; init; } = string.Empty;

        /// <summary>
        /// Permission being checked (e.g., "users:read", "projects:write")
        /// </summary>
        public string Permission { get; init; } = string.Empty;

        /// <summary>
        /// Resource type being accessed (e.g., "users", "projects")
        /// </summary>
        public string Resource { get; init; } = string.Empty;

        /// <summary>
        /// Action being performed (e.g., "read", "write", "delete")
        /// </summary>
        public string Action { get; init; } = string.Empty;

        /// <summary>
        /// Additional context for fine-grained permission validation
        /// </summary>
        public PermissionContext? Context { get; init; }

        /// <summary>
        /// Whether to include inherited permissions from parent organizations
        /// </summary>
        public bool IncludeInherited { get; init; } = true;

        /// <summary>
        /// Whether to return detailed information about why permission was granted/denied
        /// </summary>
        public bool IncludeReasonDetails { get; init; } = false;

        /// <summary>
        /// Alternative permissions that would also satisfy this request
        /// </summary>
        public List<string> AlternativePermissions { get; init; } = new();

        /// <summary>
        /// Source of the permission check (e.g., "mcp_task", "api_endpoint")
        /// </summary>
        public string Source { get; init; } = "mcp_task";

        /// <summary>
        /// IP address of the requesting client
        /// </summary>
        public string? IpAddress { get; init; }

        /// <summary>
        /// Session ID for correlation
        /// </summary>
        public string? SessionId { get; init; }

        /// <summary>
        /// Timestamp when the request was created
        /// </summary>
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Unique request ID for tracing
        /// </summary>
        public string RequestId { get; init; } = Guid.NewGuid().ToString();

        public ValidatePermissionRequest() { }

        public ValidatePermissionRequest(
            string userId,
            string organizationId,
            string permission,
            string resource,
            string action,
            PermissionContext? context = null,
            bool includeInherited = true,
            bool includeReasonDetails = false,
            string source = "mcp_task",
            string? ipAddress = null,
            string? sessionId = null)
        {
            UserId = userId;
            OrganizationId = organizationId;
            Permission = permission;
            Resource = resource;
            Action = action;
            Context = context;
            IncludeInherited = includeInherited;
            IncludeReasonDetails = includeReasonDetails;
            Source = source;
            IpAddress = ipAddress;
            SessionId = sessionId;
        }

        /// <summary>
        /// Creates a simple permission validation request
        /// </summary>
        public static ValidatePermissionRequest Simple(
            string userId,
            string organizationId,
            string resource,
            string action,
            string? resourceId = null,
            string source = "mcp_task")
        {
            var permission = $"{resource}:{action}";
            var context = resourceId != null 
                ? new PermissionContext { ResourceId = resourceId }
                : null;

            return new ValidatePermissionRequest(
                userId, organizationId, permission, resource, action,
                context: context, source: source);
        }

        /// <summary>
        /// Creates a permission validation request with context
        /// </summary>
        public static ValidatePermissionRequest WithContext(
            string userId,
            string organizationId,
            string resource,
            string action,
            PermissionContext context,
            string source = "mcp_task")
        {
            var permission = $"{resource}:{action}";
            
            return new ValidatePermissionRequest(
                userId, organizationId, permission, resource, action,
                context: context, source: source);
        }
    }
}