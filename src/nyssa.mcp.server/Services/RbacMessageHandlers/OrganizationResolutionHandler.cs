using MassTransit;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Dapper;
using System.Data;

namespace Nyssa.Mcp.Server.Services.RbacMessageHandlers
{
    /// <summary>
    /// Message handler for organization resolution operations
    /// </summary>
    public class OrganizationResolutionHandler : IConsumer<GetUserOrganizationsRequest>
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly ILogger<OrganizationResolutionHandler> _logger;

        public OrganizationResolutionHandler(
            IDatabaseConnectionFactory connectionFactory,
            ILogger<OrganizationResolutionHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <summary>
        /// Handles user organization resolution requests
        /// </summary>
        public async Task Consume(ConsumeContext<GetUserOrganizationsRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing organization resolution request for user: {UserId}", request.UserId);

            try
            {
                var organizationsResult = await ResolveUserOrganizationsAsync(request);
                
                if (organizationsResult.Success)
                {
                    var organizations = organizationsResult.Value;
                    await context.RespondAsync(organizations);
                    _logger.LogInformation("Resolved {OrganizationCount} organizations for user {UserId}", 
                        organizations.Organizations.Count, request.UserId);
                }
                else
                {
                    _logger.LogError("Failed to resolve organizations for user {UserId}: {Errors}", 
                        request.UserId, string.Join(", ", organizationsResult.Errors.Select(e => e.Text)));
                    throw new InvalidOperationException($"Failed to resolve organizations: {string.Join(", ", organizationsResult.Errors.Select(e => e.Text))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve organizations for user {UserId}: {Error}", 
                    request.UserId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Resolves user organizations from the database
        /// </summary>
        private async Task<Result<GetUserOrganizationsResponse>> ResolveUserOrganizationsAsync(GetUserOrganizationsRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<GetUserOrganizationsResponse>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                // Get user organizations with memberships
                var organizations = await GetUserOrganizationsFromDatabase(connection, request);

                // If no organizations found
                if (!organizations.Any())
                {
                    var emptyResponse = new GetUserOrganizationsResponse(
                        userId: request.UserId,
                        organizations: new List<UserOrganizationMembership>(),
                        totalCount: 0,
                        isLimited: false,
                        requestId: request.RequestId);

                    return Result<GetUserOrganizationsResponse>.Ok(emptyResponse);
                }

                // Apply limit if specified
                var totalCount = organizations.Count;
                var isLimited = false;
                
                if (request.Limit > 0 && request.Limit < totalCount)
                {
                    organizations = organizations.Take(request.Limit).ToList();
                    isLimited = true;
                }

                var response = new GetUserOrganizationsResponse(
                    userId: request.UserId,
                    organizations: organizations,
                    totalCount: totalCount,
                    isLimited: isLimited,
                    requestId: request.RequestId);

                return Result<GetUserOrganizationsResponse>.Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error resolving organizations for user {UserId}: {Error}", 
                    request.UserId, ex.Message);
                return Result<GetUserOrganizationsResponse>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Gets user organizations from database
        /// </summary>
        private async Task<List<UserOrganizationMembership>> GetUserOrganizationsFromDatabase(
            IDbConnection connection, 
            GetUserOrganizationsRequest request)
        {
            var organizationData = await GetOrganizationMemberships(connection, request);
            var organizationMemberships = new List<UserOrganizationMembership>();

            foreach (var orgGroup in organizationData.GroupBy(od => od.OrganizationId))
            {
                var firstOrg = orgGroup.First();
                
                // Get roles for this organization
                var roles = orgGroup.Select(od => new UserRoleInfo
                {
                    RoleId = od.RoleId,
                    RoleName = od.RoleName,
                    RoleDescription = od.RoleDescription,
                    IsInheritable = od.IsInheritable,
                    AssignedAt = od.RoleAssignedAt
                }).ToList();

                // Get hierarchy info if requested
                OrganizationHierarchyInfo? parent = null;
                List<OrganizationHierarchyInfo> children = new();

                if (request.IncludeHierarchy)
                {
                    parent = await GetParentOrganization(connection, firstOrg.OrganizationPath);
                    children = await GetChildOrganizations(connection, firstOrg.OrganizationPath);
                }

                var membership = new UserOrganizationMembership
                {
                    OrganizationId = firstOrg.OrganizationId,
                    OrganizationName = firstOrg.OrganizationName,
                    OrganizationPath = firstOrg.OrganizationPath,
                    IsPrimary = firstOrg.IsPrimary,
                    Roles = roles,
                    JoinedAt = firstOrg.JoinedAt,
                    Status = firstOrg.Status,
                    Description = firstOrg.OrganizationDescription,
                    Parent = parent,
                    Children = children
                };

                organizationMemberships.Add(membership);
            }

            return organizationMemberships.OrderBy(om => om.OrganizationName).ToList();
        }

        /// <summary>
        /// Gets organization memberships data
        /// </summary>
        private async Task<List<OrganizationMembershipData>> GetOrganizationMemberships(
            IDbConnection connection, 
            GetUserOrganizationsRequest request)
        {
            var query = @"
                SELECT 
                    o.id as organization_id,
                    o.name as organization_name,
                    o.path as organization_path,
                    o.description as organization_description,
                    om.is_primary,
                    om.joined_at,
                    om.status,
                    r.id as role_id,
                    r.name as role_name,
                    r.description as role_description,
                    r.is_inheritable,
                    ur.assigned_at as role_assigned_at
                FROM authz.organization_memberships om
                JOIN authz.organizations o ON o.id = om.organization_id
                LEFT JOIN authz.user_roles ur ON ur.user_id = om.user_id AND ur.organization_id = om.organization_id AND ur.deleted_at IS NULL
                LEFT JOIN authz.roles r ON r.id = ur.role_id AND r.deleted_at IS NULL
                WHERE om.user_id = @UserId 
                AND om.deleted_at IS NULL
                AND o.deleted_at IS NULL";

            object parameters = new { UserId = request.UserId };

            // Add status filter if specified
            if (!string.IsNullOrEmpty(request.StatusFilter))
            {
                query += " AND om.status = @StatusFilter";
                parameters = new { UserId = request.UserId, StatusFilter = request.StatusFilter };
            }

            query += " ORDER BY o.name, r.name";

            var organizationData = await connection.QueryAsync<OrganizationMembershipData>(query, parameters);
            return organizationData.ToList();
        }

        /// <summary>
        /// Gets parent organization info
        /// </summary>
        private async Task<OrganizationHierarchyInfo?> GetParentOrganization(IDbConnection connection, string organizationPath)
        {
            const string query = @"
                SELECT 
                    id as organization_id,
                    name as organization_name,
                    path as organization_path
                FROM authz.organizations 
                WHERE path = subpath(@Path, 0, nlevel(@Path) - 1)
                AND deleted_at IS NULL";

            var parent = await connection.QuerySingleOrDefaultAsync<OrganizationHierarchyData>(query, new { Path = organizationPath });
            
            return parent != null ? new OrganizationHierarchyInfo
            {
                OrganizationId = parent.OrganizationId,
                OrganizationName = parent.OrganizationName,
                OrganizationPath = parent.OrganizationPath
            } : null;
        }

        /// <summary>
        /// Gets child organizations
        /// </summary>
        private async Task<List<OrganizationHierarchyInfo>> GetChildOrganizations(IDbConnection connection, string organizationPath)
        {
            const string query = @"
                SELECT 
                    id as organization_id,
                    name as organization_name,
                    path as organization_path
                FROM authz.organizations 
                WHERE path <@ @Path 
                AND path != @Path
                AND nlevel(path) = nlevel(@Path) + 1
                AND deleted_at IS NULL
                ORDER BY name";

            var children = await connection.QueryAsync<OrganizationHierarchyData>(query, new { Path = organizationPath });
            
            return children.Select(c => new OrganizationHierarchyInfo
            {
                OrganizationId = c.OrganizationId,
                OrganizationName = c.OrganizationName,
                OrganizationPath = c.OrganizationPath
            }).ToList();
        }

        /// <summary>
        /// Database model for organization membership data
        /// </summary>
        private record OrganizationMembershipData
        {
            public string OrganizationId { get; init; } = string.Empty;
            public string OrganizationName { get; init; } = string.Empty;
            public string OrganizationPath { get; init; } = string.Empty;
            public string? OrganizationDescription { get; init; }
            public bool IsPrimary { get; init; }
            public DateTime JoinedAt { get; init; }
            public string Status { get; init; } = "Active";
            public string RoleId { get; init; } = string.Empty;
            public string RoleName { get; init; } = string.Empty;
            public string? RoleDescription { get; init; }
            public bool IsInheritable { get; init; }
            public DateTime RoleAssignedAt { get; init; }
        }

        /// <summary>
        /// Database model for organization hierarchy data
        /// </summary>
        private record OrganizationHierarchyData
        {
            public string OrganizationId { get; init; } = string.Empty;
            public string OrganizationName { get; init; } = string.Empty;
            public string OrganizationPath { get; init; } = string.Empty;
        }
    }
}