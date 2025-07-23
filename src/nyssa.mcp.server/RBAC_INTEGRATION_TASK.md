# Nyssa RBAC Integration Implementation Task

## Task Overview

Implement the integration between the existing WorkOS OIDC authentication system and the PostgreSQL RBAC database within the MCP server architecture using MassTransit as the message bus. Transform basic authentication into a permission-based authorization system with embedded permissions in JWT tokens.

## Current System State

### Existing Components
- **WorkOS OIDC Authentication**: Fully functional OAuth 2.0 flow with authorization code exchange
- **PostgreSQL RBAC Database**: Complete schema with tables, functions, and views for permission management
- **MCP Server**: Basic token exchange with WorkOS via MCP tasks
- **Blazor WASM Client**: Authentication state management and UI
- **MassTransit**: Message bus for inter-service communication

### Database Schema Available
- `authz.users` - User accounts with external_id linking to WorkOS
- `authz.organizations` - Hierarchical organization structure using LTREE
- `authz.organization_memberships` - User-organization relationships
- `authz.roles` - Organization-specific roles with inheritance
- `authz.permissions` - System-wide permission definitions
- `authz.role_permissions` - Role-permission mappings
- `authz.user_roles` - User-role assignments within organizations
- `authz.token_blacklist` - JWT revocation tracking
- `authz.audit_events` - Immutable audit logging

### Key Database Functions Available
- `authz.resolve_user_permissions(user_id, org_id, include_inherited)` - Get all user permissions
- `authz.check_user_permission(user_id, org_id, permission)` - Check specific permission
- `authz.blacklist_token(jti, reason)` - Revoke JWT token
- `authz.is_token_blacklisted(jti)` - Check if token is revoked
- `authz.log_audit_event(user_id, event_type, details, category, source, ip_address)` - Log events

## Intended System State

### MCP Task-Based Architecture
The current HTTP endpoint-based flow should be transformed to use MCP tasks and MassTransit messaging:

1. **WorkOS Authentication** (existing) → Get basic user identity
2. **RBAC User Resolution** (new) → Link WorkOS user to RBAC user via MCP task
3. **Permission Resolution** (new) → Query user permissions via MassTransit message
4. **Scoped Token Generation** (new) → Create JWT with embedded permissions
5. **Audit Logging** (new) → Log authentication events via MassTransit

### Token Evolution
- **Authorization Code**: Temporary code from WorkOS (unchanged)
- **WorkOS Access Token**: Intermediate token with basic user info (unchanged)
- **Scoped JWT Token**: New final token with embedded RBAC permissions

### Scoped JWT Structure
```json
{
  "iss": "nyssa-mcp-server",
  "sub": "workos_user_id",
  "aud": "nyssa-api",
  "exp": 1640995200,
  "iat": 1640991600,
  "jti": "jwt_scoped_123456789",
  "user": {
    "id": "rbac_user_uuid",
    "email": "user@company.com",
    "name": "John Doe"
  },
  "organization": {
    "id": "org_uuid",
    "name": "Engineering Department",
    "path": "company.engineering"
  },
  "permissions": [
    "users:read",
    "users:write",
    "tasks:read",
    "tasks:write",
    "projects:admin"
  ],
  "roles": [
    {
      "id": "role_uuid",
      "name": "Engineering Manager",
      "is_inheritable": true
    }
  ],
  "token_type": "scoped",
  "scope": "org:org_uuid"
}
```

## Complete Implementation Sequence Diagram

```mermaid
sequenceDiagram
    participant WASM as Blazor WASM Client
    participant MCP as MCP Server
    participant WorkOS as WorkOS OIDC
    participant Browser as Browser Storage
    participant RBAC as PostgreSQL RBAC
    participant Bus as MassTransit Bus

    Note over WASM, RBAC: Enhanced MCP Task-Based Authentication & Authorization Flow

    WASM->>WASM: 1. User clicks "Login"
    WASM->>MCP: 2. MCP Task: get_auth_url()
    MCP->>MCP: 3. Generate state parameter
    MCP->>MCP: 4. Build WorkOS authorization URL
    MCP-->>WASM: 5. Return authorization URL
    WASM->>WorkOS: 6. Redirect to WorkOS (browser navigation)
    
    Note over WorkOS: User authenticates with WorkOS
    
    WorkOS-->>WASM: 7. Redirect to /auth/callback with auth code
    WASM->>WASM: 8. Extract authorization code from URL
    WASM->>MCP: 9. MCP Task: exchange_code_for_token(auth_code)
    MCP->>WorkOS: 10. Exchange code for WorkOS access token
    WorkOS-->>MCP: 11. Return WorkOS token + user profile
    
    Note over MCP, RBAC: RBAC Integration Phase via MassTransit
    
    MCP->>MCP: 12. Extract user ID from WorkOS token
    MCP->>Bus: 13. Publish: ResolveUserRequest(workos_user_id)
    Bus->>RBAC: 14. Consume: ResolveUserRequest
    RBAC-->>Bus: 15. Publish: UserResolvedEvent(user_data)
    Bus->>MCP: 16. Consume: UserResolvedEvent
    
    alt New User
        MCP->>Bus: 17a. Publish: CreateUserRequest(workos_user_data)
        Bus->>RBAC: 18a. Consume: CreateUserRequest
        RBAC-->>Bus: 19a. Publish: UserCreatedEvent(new_user_data)
        Bus->>MCP: 20a. Consume: UserCreatedEvent
    end
    
    MCP->>Bus: 21. Publish: GetUserOrganizationsRequest(user_id)
    Bus->>RBAC: 22. Consume: GetUserOrganizationsRequest
    RBAC-->>Bus: 23. Publish: UserOrganizationsResolvedEvent(org_list)
    Bus->>MCP: 24. Consume: UserOrganizationsResolvedEvent
    MCP->>MCP: 25. Select primary organization
    
    MCP->>Bus: 26. Publish: GetUserPermissionsRequest(user_id, org_id)
    Bus->>RBAC: 27. Consume: GetUserPermissionsRequest
    RBAC-->>Bus: 28. Publish: UserPermissionsResolvedEvent(permissions)
    Bus->>MCP: 29. Consume: UserPermissionsResolvedEvent
    
    MCP->>MCP: 30. Generate scoped JWT with embedded permissions
    MCP->>Bus: 31. Publish: LogAuthenticationEventRequest(event_data)
    Bus->>RBAC: 32. Consume: LogAuthenticationEventRequest
    MCP-->>WASM: 33. Return scoped token + user profile
    
    Note over WASM, Browser: Client-side Storage
    
    WASM->>Browser: 34. Store scoped token in localStorage
    WASM->>WASM: 35. Update authentication state
    WASM->>WASM: 36. Redirect to /app dashboard
    
    Note over WASM, RBAC: API Authorization Flow via MCP Tasks
    
    WASM->>MCP: 37. MCP Task: validate_token_and_authorize(token, resource, action)
    MCP->>MCP: 38. Validate JWT signature
    MCP->>Bus: 39. Publish: CheckTokenBlacklistRequest(jti)
    Bus->>RBAC: 40. Consume: CheckTokenBlacklistRequest
    RBAC-->>Bus: 41. Publish: TokenBlacklistCheckedEvent(is_blacklisted)
    Bus->>MCP: 42. Consume: TokenBlacklistCheckedEvent
    MCP->>MCP: 43. Extract permissions from JWT
    MCP->>MCP: 44. Authorize request based on permissions
    MCP-->>WASM: 45. Return authorization result
```

## Implementation Requirements

### 1. MCP Server Enhancements

#### New MCP Tasks Required
- **`exchange_code_for_token`**: Enhanced to include RBAC integration
- **`validate_token_and_authorize`**: Token validation and permission checking
- **`revoke_token`**: Token blacklisting via MassTransit
- **`get_user_permissions`**: Direct permission querying
- **`get_user_organizations`**: Organization resolution

#### New MassTransit Messages Required
- **Authentication Messages**:
  - `ResolveUserRequest/Response`
  - `CreateUserRequest/Response`
  - `GetUserOrganizationsRequest/Response`
  - `GetUserPermissionsRequest/Response`
  - `LogAuthenticationEventRequest`

- **Authorization Messages**:
  - `CheckTokenBlacklistRequest/Response`
  - `BlacklistTokenRequest`
  - `ValidatePermissionRequest/Response`

#### New Services Required
- **RbacMessageHandlers**: MassTransit consumers for RBAC operations
- **JwtService**: JWT token generation and validation
- **Enhanced AuthenticationTools**: MCP task implementations
- **MessagePublishers**: MassTransit publishers for async operations

#### New Models Required
- **RbacUser**: Internal user representation
- **UserPermission**: Permission data structure
- **ScopedToken**: JWT payload structure
- **OrganizationInfo**: Organization data
- **RoleInfo**: Role data
- **MassTransit Messages**: Request/response/event models

### 2. MassTransit Configuration

#### Bus Configuration
- **RabbitMQ/In-Memory**: Configure message bus transport
- **Message Routing**: Define message routing and queues
- **Retry Policies**: Configure retry logic for database operations
- **Circuit Breaker**: Implement circuit breaker for database failures
- **Message Serialization**: JSON serialization for all messages

#### Consumer Configuration
- **Concurrent Consumers**: Configure parallel message processing
- **Prefetch Count**: Optimize message prefetching
- **Error Handling**: Dead letter queues for failed messages
- **Health Checks**: Monitor consumer health

### 3. Database Integration via MassTransit

#### Message Handlers
- **UserResolutionHandler**: Handle user lookup and creation
- **PermissionResolutionHandler**: Handle permission queries
- **OrganizationResolutionHandler**: Handle organization queries
- **TokenManagementHandler**: Handle token blacklisting
- **AuditLoggingHandler**: Handle audit event logging

#### Async Database Operations
- **User Resolution**: Async user lookup by external_id
- **Permission Resolution**: Async permission queries with inheritance
- **Organization Resolution**: Async organization hierarchy queries
- **Token Operations**: Async token blacklisting and validation
- **Audit Logging**: Async audit event persistence

### 4. MCP Task Authorization

#### Authorization Flow
1. **Token Extraction**: Parse JWT from MCP task parameters
2. **Token Validation**: Verify signature and expiration via MassTransit
3. **Blacklist Check**: Ensure token isn't revoked via message
4. **Permission Extraction**: Parse permissions from JWT claims
5. **Request Authorization**: Check required permissions
6. **Response**: Allow or deny based on permissions

#### Permission Checking via MCP Tasks
- **Resource-based**: `resource:action` format (e.g., `users:read`)
- **Wildcard Support**: Handle permission patterns
- **Role-based**: Support role-based access control
- **Organization-scoped**: Respect organization boundaries

### 5. Error Handling

#### MassTransit Error Handling
- **Message Retry**: Retry failed database operations
- **Dead Letter Queues**: Handle permanently failed messages
- **Circuit Breaker**: Prevent cascade failures
- **Error Logging**: Log all message processing errors

#### MCP Task Error Handling
- **Authentication Errors**: Handle new WorkOS users
- **No Organizations**: Handle users without org access
- **No Permissions**: Handle users with no permissions
- **Database Errors**: Graceful handling of DB failures via messages

### 6. Security Considerations

#### Token Security
- **Secure Signing**: Use strong JWT secret keys
- **Token Expiration**: Implement proper expiration handling
- **Token Revocation**: Support immediate token blacklisting via messages
- **Audit Trail**: Log all authentication and authorization events

#### Message Security
- **Message Encryption**: Encrypt sensitive message payloads
- **Message Authentication**: Verify message integrity
- **Access Control**: Control who can publish/consume messages
- **Audit Logging**: Log all message processing events

## Success Criteria

### Functional Requirements
- ✅ WorkOS authentication flow remains unchanged
- ✅ RBAC integration adds permission-based authorization via MassTransit
- ✅ Scoped JWT tokens contain embedded permissions
- ✅ MCP tasks enforce permission-based access control
- ✅ Token revocation works immediately via messages
- ✅ Audit logging captures all events via MassTransit

### Performance Requirements
- ✅ Token validation < 5ms (using embedded permissions)
- ✅ Permission resolution < 100ms (async database queries)
- ✅ Authentication flow < 2 seconds total
- ✅ MCP task authorization < 10ms per request
- ✅ Message processing < 50ms per message

### Scalability Requirements
- ✅ Horizontal scaling via MassTransit consumers
- ✅ Database connection pooling
- ✅ Message queue persistence
- ✅ Load balancing across consumers

### Security Requirements
- ✅ JWT tokens are cryptographically signed
- ✅ Token blacklisting works immediately via messages
- ✅ Permission inheritance is secure
- ✅ Audit trail is immutable
- ✅ Organization isolation is enforced
- ✅ Message security and encryption

## Implementation Priority

### Phase 1: Core MassTransit Integration
1. MassTransit bus configuration
2. Message models and contracts
3. Basic message handlers
4. Enhanced MCP task implementations

### Phase 2: RBAC Message Integration
1. User resolution messages
2. Permission resolution messages
3. Organization resolution messages
4. Token management messages

### Phase 3: Authorization & Security
1. MCP task authorization middleware
2. Token validation and blacklisting
3. Audit logging integration
4. Security hardening and encryption

## File Structure

```
src/nyssa.mcp.server/
├── Models/
│   ├── RbacUser.cs
│   ├── UserPermission.cs
│   ├── ScopedToken.cs
│   ├── OrganizationInfo.cs
│   ├── RoleInfo.cs
│   └── Messages/
│       ├── Authentication/
│       │   ├── ResolveUserRequest.cs
│       │   ├── ResolveUserResponse.cs
│       │   ├── CreateUserRequest.cs
│       │   ├── CreateUserResponse.cs
│       │   ├── GetUserOrganizationsRequest.cs
│       │   ├── GetUserOrganizationsResponse.cs
│       │   ├── GetUserPermissionsRequest.cs
│       │   ├── GetUserPermissionsResponse.cs
│       │   └── LogAuthenticationEventRequest.cs
│       └── Authorization/
│           ├── CheckTokenBlacklistRequest.cs
│           ├── CheckTokenBlacklistResponse.cs
│           ├── BlacklistTokenRequest.cs
│           └── ValidatePermissionRequest.cs
├── Services/
│   ├── RbacMessageHandlers/
│   │   ├── UserResolutionHandler.cs
│   │   ├── PermissionResolutionHandler.cs
│   │   ├── OrganizationResolutionHandler.cs
│   │   ├── TokenManagementHandler.cs
│   │   └── AuditLoggingHandler.cs
│   ├── JwtService.cs
│   ├── MessagePublishers/
│   │   ├── AuthenticationPublisher.cs
│   │   └── AuthorizationPublisher.cs
│   └── Enhanced WorkOSAuthenticationService.cs
├── Tools/
│   └── Enhanced AuthenticationTools.cs
├── Configuration/
│   ├── MassTransitConfiguration.cs
│   └── DatabaseConfiguration.cs
└── Program.cs (updated with MassTransit)
```

## Implementation Task Breakdown

### 🚀 **Phase 1: Core MassTransit Integration** (High Priority)
1. **Phase 1.1**: Configure MassTransit bus with RabbitMQ/In-Memory transport
2. **Phase 1.2**: Create core message models and contracts for authentication
3. **Phase 1.3**: Create core message models and contracts for authorization
4. **Phase 1.4**: Implement basic MassTransit message handlers

### 🔗 **Phase 2: RBAC Message Integration** (Medium Priority)
5. **Phase 2.1**: Implement user resolution messages and handlers
6. **Phase 2.2**: Implement permission resolution messages and handlers
7. **Phase 2.3**: Implement organization resolution messages and handlers
8. **Phase 2.4**: Implement token management messages and handlers

### 🔐 **Phase 3: Authorization & Security** (Medium/Low Priority)
9. **Phase 3.1**: Create JWT service for scoped token generation
10. **Phase 3.2**: Enhance WorkOS authentication service with RBAC integration
11. **Phase 3.3**: Implement MCP task authorization middleware
12. **Phase 3.4**: Add token validation and blacklisting via MassTransit
13. **Phase 3.5**: Implement audit logging integration via MassTransit
14. **Phase 3.6**: Add security hardening and message encryption

### 📋 **Recommended Approach**
**Start with Phase 1** - it establishes the foundational messaging infrastructure that everything else depends on. Each task builds on the previous ones, so following the sequence will minimize integration issues.

## Error Handling Requirements

### Result Pattern Implementation
All operations in this RBAC integration **MUST** use the Result pattern for error handling as defined in `.cursor/rules/result-pattern-rules.md`:

- **All methods that can fail** should return `Result<T, ErrorMessage>` instead of throwing exceptions
- **ErrorMessage structure**: Contains `Code` (HTTP-style), `Text` (technical), and `UserFriendlyText` (user-facing)
- **Error code categories** for RBAC operations:
  - `4001-4099`: Authentication failures (WorkOS, token validation)
  - `4100-4199`: Authorization failures (permission checks, role validation)
  - `4200-4299`: RBAC validation errors (user resolution, organization membership)
  - `5001-5099`: Database operation failures (PostgreSQL connectivity, query failures)
  - `5100-5199`: Message bus failures (MassTransit connectivity, message processing)
  - `5200-5299`: External service failures (WorkOS API, JWT service)

### Result Pattern Usage Examples
```csharp
// Service method signature
public async Task<Result<ScopedToken, ErrorMessage>> GenerateScopedTokenAsync(string workosUserId, string organizationId)

// Error handling in message handlers
public async Task<Result<UserPermissions, ErrorMessage>> Handle(GetUserPermissionsRequest request)
{
    var userResult = await _userRepository.GetUserByExternalIdAsync(request.WorkOSUserId);
    if (!userResult.Success)
        return userResult.Errors; // Propagate repository errors

    return await _permissionService.ResolveUserPermissionsAsync(userResult.Value.Id, request.OrganizationId);
}

// Result chaining with Then()
var result = await ResolveUserAsync(workosUserId)
    .Then(user => GetUserOrganizationsAsync(user.Id))
    .Then(orgs => SelectPrimaryOrganizationAsync(orgs))
    .Then(org => ResolveUserPermissionsAsync(user.Id, org.Id))
    .Then(permissions => GenerateScopedTokenAsync(user, org, permissions));
```

## Dependencies Required

### NuGet Packages
- `MassTransit`
- `MassTransit.RabbitMQ` (or `MassTransit.InMemory`)
- `MassTransit.Newtonsoft` (or `MassTransit.SystemTextJson`)
- `Microsoft.IdentityModel.Tokens`
- `System.IdentityModel.Tokens.Jwt`
- `Npgsql`
- `Dapper`

### Configuration
- **Database Connection String**: PostgreSQL RBAC database
- **JWT Secret Key**: For token signing
- **MassTransit Transport**: RabbitMQ or In-Memory
- **Message Queue Settings**: Retry policies, circuit breakers

This implementation will transform the basic authentication system into a comprehensive, secure, and scalable authorization system that leverages MassTransit for reliable message-based communication with the PostgreSQL RBAC database. All operations will use the Result pattern for robust, explicit error handling. 