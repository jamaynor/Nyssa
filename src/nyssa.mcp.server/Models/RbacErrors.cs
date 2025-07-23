namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Predefined error messages for common RBAC operations
    /// </summary>
    public static class RbacErrors
    {
        // Authentication Errors (4001-4099)
        public static class Authentication
        {
            public static readonly ErrorMessage InvalidToken = ErrorMessage.Authentication(
                4001, 
                "JWT token is invalid or malformed", 
                "Your session is invalid. Please log in again.");

            public static readonly ErrorMessage TokenExpired = ErrorMessage.Authentication(
                4002, 
                "JWT token has expired", 
                "Your session has expired. Please log in again.");

            public static readonly ErrorMessage WorkOsTokenExchangeFailed = ErrorMessage.Authentication(
                4003, 
                "Failed to exchange authorization code with WorkOS", 
                "Authentication failed. Please try logging in again.");

            public static readonly ErrorMessage WorkOsUserNotFound = ErrorMessage.Authentication(
                4004, 
                "User profile not found in WorkOS response", 
                "Unable to retrieve your user information. Please contact support.");

            public static readonly ErrorMessage InvalidAuthorizationCode = ErrorMessage.Authentication(
                4005, 
                "Authorization code is invalid or has expired", 
                "Login failed. Please try again.");

            public static readonly ErrorMessage UserNotFound = ErrorMessage.Authentication(
                4006, 
                "User not found in the system", 
                "User account not found. Please contact support.");
        }

        // Authorization Errors (4100-4199)
        public static class Authorization
        {
            public static readonly ErrorMessage InsufficientPermissions = ErrorMessage.Authorization(
                4100, 
                "User does not have required permissions for this operation", 
                "You don't have permission to perform this action.");

            public static readonly ErrorMessage TokenBlacklisted = ErrorMessage.Authorization(
                4101, 
                "JWT token has been revoked and is blacklisted", 
                "Your session has been revoked. Please log in again.");

            public static readonly ErrorMessage OrganizationAccessDenied = ErrorMessage.Authorization(
                4102, 
                "User does not have access to the specified organization", 
                "You don't have access to this organization.");

            public static readonly ErrorMessage RoleNotFound = ErrorMessage.Authorization(
                4103, 
                "Specified role does not exist or user does not have this role", 
                "The requested role was not found.");

            public static ErrorMessage MissingPermission(string permission) => ErrorMessage.Authorization(
                4104, 
                $"Missing required permission: {permission}", 
                "You don't have the required permissions for this action.");

            public static readonly ErrorMessage OrganizationNotFound = ErrorMessage.Authorization(
                4105, 
                "Organization not found or access denied", 
                "The specified organization was not found or you don't have access to it.");

            public static readonly ErrorMessage TokenBlacklistFailed = ErrorMessage.Authorization(
                4106, 
                "Failed to blacklist token", 
                "Unable to revoke the session. Please try again.");
        }

        // RBAC Validation Errors (4200-4299)
        public static class RbacValidation
        {
            public static readonly ErrorMessage UserNotFound = ErrorMessage.RbacValidation(
                4200, 
                "User not found in RBAC system", 
                "User account not found. Please contact support.");

            public static readonly ErrorMessage OrganizationNotFound = ErrorMessage.RbacValidation(
                4201, 
                "Organization not found in RBAC system", 
                "The specified organization was not found.");

            public static readonly ErrorMessage NoOrganizationMembership = ErrorMessage.RbacValidation(
                4202, 
                "User has no organization memberships", 
                "You don't belong to any organizations. Please contact an administrator.");

            public static readonly ErrorMessage InvalidOrganizationPath = ErrorMessage.RbacValidation(
                4203, 
                "Organization path is invalid or malformed", 
                "Invalid organization specified.");

            public static readonly ErrorMessage UserCreationFailed = ErrorMessage.RbacValidation(
                4204, 
                "Failed to create user in RBAC system", 
                "Unable to set up your account. Please try again or contact support.");

            public static ErrorMessage InvalidExternalUserId(string externalId) => ErrorMessage.RbacValidation(
                4205, 
                $"Invalid external user ID format: {externalId}", 
                "Invalid user identifier provided.");
        }

        // Database Errors (5001-5099)
        public static class Database
        {
            public static readonly ErrorMessage ConnectionFailed = ErrorMessage.Database(
                5001, 
                "Failed to connect to PostgreSQL database", 
                "A system error occurred. Please try again later.");

            public static readonly ErrorMessage QueryExecutionFailed = ErrorMessage.Database(
                5002, 
                "Database query execution failed", 
                "A system error occurred. Please try again later.");

            public static readonly ErrorMessage TransactionFailed = ErrorMessage.Database(
                5003, 
                "Database transaction failed and was rolled back", 
                "Operation failed. Please try again later.");

            public static readonly ErrorMessage ConstraintViolation = ErrorMessage.Database(
                5004, 
                "Database constraint violation occurred", 
                "The operation violates system constraints. Please check your data and try again.");

            public static ErrorMessage CustomQueryError(string operation, string details) => ErrorMessage.Database(
                5005,
                $"Database operation '{operation}' failed: {details}",
                "A system error occurred. Please try again later.");
        }

        // Message Bus Errors (5100-5199)
        public static class MessageBus
        {
            public static readonly ErrorMessage PublishFailed = ErrorMessage.MessageBus(
                5100, 
                "Failed to publish message to MassTransit bus", 
                "A system error occurred. Please try again later.");

            public static readonly ErrorMessage ConsumeFailed = ErrorMessage.MessageBus(
                5101, 
                "Failed to consume message from MassTransit bus", 
                "A system error occurred. Please try again later.");

            public static readonly ErrorMessage TimeoutError = ErrorMessage.MessageBus(
                5102, 
                "Message processing timed out", 
                "The operation is taking longer than expected. Please try again later.");

            public static readonly ErrorMessage SerializationError = ErrorMessage.MessageBus(
                5103, 
                "Failed to serialize/deserialize message", 
                "A system error occurred. Please try again later.");

            public static ErrorMessage CustomMessageError(string operation, string details) => ErrorMessage.MessageBus(
                5104,
                $"Message bus operation '{operation}' failed: {details}",
                "A system error occurred. Please try again later.");
        }

        // External Service Errors (5200-5299)
        public static class ExternalService
        {
            public static readonly ErrorMessage WorkOsApiError = ErrorMessage.ExternalService(
                5200, 
                "WorkOS API request failed", 
                "Authentication service is temporarily unavailable. Please try again later.");

            public static readonly ErrorMessage JwtSigningError = ErrorMessage.ExternalService(
                5201, 
                "Failed to sign JWT token", 
                "A system error occurred. Please try again later.");

            public static readonly ErrorMessage JwtValidationError = ErrorMessage.ExternalService(
                5202, 
                "Failed to validate JWT token signature", 
                "Your session could not be validated. Please log in again.");

            public static readonly ErrorMessage ExternalServiceTimeout = ErrorMessage.ExternalService(
                5203, 
                "External service request timed out", 
                "External service is temporarily unavailable. Please try again later.");

            public static ErrorMessage CustomServiceError(string service, string operation, string details) => ErrorMessage.ExternalService(
                5204,
                $"External service '{service}' operation '{operation}' failed: {details}",
                "An external service is temporarily unavailable. Please try again later.");
        }
    }
}