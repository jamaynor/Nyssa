using MassTransit;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Dapper;
using System.Data;

namespace Nyssa.Mcp.Server.Services.RbacMessageHandlers
{
    /// <summary>
    /// Message handler for permission resolution operations
    /// </summary>
    public class PermissionResolutionHandler : IConsumer<GetUserPermissionsRequest>
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly ILogger<PermissionResolutionHandler> _logger;

        public PermissionResolutionHandler(
            IDatabaseConnectionFactory connectionFactory,
            ILogger<PermissionResolutionHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <summary>
        /// Handles user permission resolution requests
        /// </summary>
        public async Task Consume(ConsumeContext<GetUserPermissionsRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing permission resolution request for user: {UserId} in organization: {OrganizationId}", 
                request.UserId, request.OrganizationId);

            try
            {
                var permissionsResult = await ResolveUserPermissionsAsync(request);
                
                if (permissionsResult.Success)
                {
                    var permissions = permissionsResult.Value;
                    await context.RespondAsync(permissions);
                    _logger.LogInformation("Resolved {PermissionCount} permissions for user {UserId} in organization {OrganizationId}", 
                        permissions.Permissions.Count, request.UserId, request.OrganizationId);
                }
                else
                {
                    _logger.LogError("Failed to resolve permissions for user {UserId} in organization {OrganizationId}: {Errors}", 
                        request.UserId, request.OrganizationId, 
                        string.Join(", ", permissionsResult.Errors.Select(e => e.Text)));
                    throw new InvalidOperationException($"Failed to resolve permissions: {string.Join(", ", permissionsResult.Errors.Select(e => e.Text))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve permissions for user {UserId} in organization {OrganizationId}: {Error}", 
                    request.UserId, request.OrganizationId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Resolves user permissions using the database function
        /// </summary>
        private async Task<Result<GetUserPermissionsResponse>> ResolveUserPermissionsAsync(GetUserPermissionsRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<GetUserPermissionsResponse>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                // First get organization info
                var orgResult = await GetOrganizationInfoAsync(connection, request.OrganizationId);
                if (!orgResult.Success)
                {
                    return Result<GetUserPermissionsResponse>.Fail(orgResult.Errors);
                }

                var organization = orgResult.Value;

                // Get user permissions using database function
                var permissions = await GetUserPermissionsFromDatabase(connection, request);

                // Get user roles
                var roles = await GetUserRolesAsync(connection, request.UserId, request.OrganizationId);

                var response = new GetUserPermissionsResponse(
                    userId: request.UserId,
                    organizationId: request.OrganizationId,
                    organizationName: organization.Name,
                    permissions: permissions,
                    roles: roles,
                    includedInherited: request.IncludeInherited,
                    requestId: request.RequestId);

                return Result<GetUserPermissionsResponse>.Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error resolving permissions for user {UserId} in organization {OrganizationId}: {Error}", 
                    request.UserId, request.OrganizationId, ex.Message);
                return Result<GetUserPermissionsResponse>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Gets organization information
        /// </summary>
        private async Task<Result<OrganizationData>> GetOrganizationInfoAsync(IDbConnection connection, string organizationId)
        {
            const string query = @"
                SELECT 
                    id,
                    name,
                    path,
                    description
                FROM authz.organizations 
                WHERE id = @OrganizationId 
                AND deleted_at IS NULL";

            var org = await connection.QuerySingleOrDefaultAsync<OrganizationData>(query, new { OrganizationId = organizationId });
            
            if (org == null)
            {
                return Result<OrganizationData>.Fail(RbacErrors.Authorization.OrganizationNotFound);
            }

            return Result<OrganizationData>.Ok(org);
        }

        /// <summary>
        /// Gets user permissions from database using the resolve function
        /// </summary>
        private async Task<List<UserPermission>> GetUserPermissionsFromDatabase(IDbConnection connection, GetUserPermissionsRequest request)
        {
            const string query = @"
                SELECT 
                    p.id as permission_id,
                    p.permission,
                    p.resource,
                    p.action,
                    p.description,
                    r.id as role_id,
                    r.name as role_name,
                    r.is_inheritable,
                    o.id as organization_id,
                    o.name as organization_name,
                    o.path as organization_path,
                    rp.granted_at,
                    CASE WHEN child_orgs.id IS NOT NULL THEN true ELSE false END as is_inherited
                FROM authz.resolve_user_permissions(@UserId, @OrganizationId, @IncludeInherited) up
                JOIN authz.permissions p ON p.id = up.permission_id
                JOIN authz.role_permissions rp ON rp.permission_id = p.id
                JOIN authz.roles r ON r.id = rp.role_id
                JOIN authz.user_roles ur ON ur.role_id = r.id AND ur.user_id = @UserId
                JOIN authz.organizations o ON o.id = ur.organization_id
                LEFT JOIN authz.organizations child_orgs ON child_orgs.path <@ o.path AND child_orgs.id = @OrganizationId AND child_orgs.id != o.id
                WHERE (@ResourceFilter IS NULL OR p.resource = @ResourceFilter)
                AND (@ActionFilter IS NULL OR p.action = @ActionFilter)
                ORDER BY p.resource, p.action";

            var permissionData = await connection.QueryAsync<PermissionData>(query, new
            {
                UserId = request.UserId,
                OrganizationId = request.OrganizationId,
                IncludeInherited = request.IncludeInherited,
                ResourceFilter = request.ResourceFilter,
                ActionFilter = request.ActionFilter
            });

            return permissionData.Select(pd => new UserPermission
            {
                PermissionId = pd.PermissionId,
                Permission = pd.Permission,
                Resource = pd.Resource,
                Action = pd.Action,
                Description = pd.Description,
                SourceRole = new PermissionSourceRole
                {
                    RoleId = pd.RoleId,
                    RoleName = pd.RoleName,
                    OrganizationId = pd.OrganizationId,
                    OrganizationName = pd.OrganizationName
                },
                SourceOrganization = new PermissionSourceOrganization
                {
                    OrganizationId = pd.OrganizationId,
                    OrganizationName = pd.OrganizationName,
                    OrganizationPath = pd.OrganizationPath,
                    IsParent = pd.IsInherited
                },
                IsInherited = pd.IsInherited,
                GrantedAt = pd.GrantedAt
            }).ToList();
        }

        /// <summary>
        /// Gets user roles in the organization
        /// </summary>
        private async Task<List<UserRoleInfo>> GetUserRolesAsync(IDbConnection connection, string userId, string organizationId)
        {
            const string query = @"
                SELECT 
                    r.id as role_id,
                    r.name as role_name,
                    r.description as role_description,
                    r.is_inheritable,
                    ur.assigned_at
                FROM authz.user_roles ur
                JOIN authz.roles r ON r.id = ur.role_id
                WHERE ur.user_id = @UserId 
                AND ur.organization_id = @OrganizationId
                AND ur.deleted_at IS NULL
                AND r.deleted_at IS NULL
                ORDER BY r.name";

            var roleData = await connection.QueryAsync<RoleData>(query, new
            {
                UserId = userId,
                OrganizationId = organizationId
            });

            return roleData.Select(rd => new UserRoleInfo
            {
                RoleId = rd.RoleId,
                RoleName = rd.RoleName,
                RoleDescription = rd.RoleDescription,
                IsInheritable = rd.IsInheritable,
                AssignedAt = rd.AssignedAt
            }).ToList();
        }

        /// <summary>
        /// Database model for organization data
        /// </summary>
        private record OrganizationData
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Path { get; init; } = string.Empty;
            public string? Description { get; init; }
        }

        /// <summary>
        /// Database model for permission data
        /// </summary>
        private record PermissionData
        {
            public string PermissionId { get; init; } = string.Empty;
            public string Permission { get; init; } = string.Empty;
            public string Resource { get; init; } = string.Empty;
            public string Action { get; init; } = string.Empty;
            public string? Description { get; init; }
            public string RoleId { get; init; } = string.Empty;
            public string RoleName { get; init; } = string.Empty;
            public bool IsInheritable { get; init; }
            public string OrganizationId { get; init; } = string.Empty;
            public string OrganizationName { get; init; } = string.Empty;
            public string OrganizationPath { get; init; } = string.Empty;
            public DateTime GrantedAt { get; init; }
            public bool IsInherited { get; init; }
        }

        /// <summary>
        /// Database model for role data
        /// </summary>
        private record RoleData
        {
            public string RoleId { get; init; } = string.Empty;
            public string RoleName { get; init; } = string.Empty;
            public string? RoleDescription { get; init; }
            public bool IsInheritable { get; init; }
            public DateTime AssignedAt { get; init; }
        }
    }
}