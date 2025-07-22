using FluentAssertions;
using Npgsql;
using Nyssa.Rbac.IntegrationTests.Setup;

namespace Nyssa.Rbac.IntegrationTests.Tests;

/// <summary>
/// Integration tests for permission resolution and inheritance
/// </summary>
public class PermissionResolutionTests : RbacTestBase
{
    public PermissionResolutionTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task ResolveUserPermissions_WithDirectRole_ShouldReturnPermissions()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");
        var roleId = await CreateTestRoleAsync(orgId, "TestRole");
        var permissionId = await CreateTestPermissionAsync("test:read");

        // Link permission to role
        await LinkPermissionToRoleAsync(roleId, permissionId);

        // Assign role to user
        await AssignUserRoleAsync(testUserId, roleId, orgId);

        // Act
        var permissionsSql = """
            SELECT permission, role_name, permission_source
            FROM authz.resolve_user_permissions(@userId, @orgId, true, NULL);
            """;

        var permissions = new List<(string Permission, string RoleName, string Source)>();
        
        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(permissionsSql, connection);
        command.Parameters.AddWithValue("@userId", testUserId);
        command.Parameters.AddWithValue("@orgId", orgId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            permissions.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)
            ));
        }

        // Assert
        permissions.Should().HaveCount(1, "User should have one permission");
        permissions[0].Permission.Should().Be("test:read");
        permissions[0].RoleName.Should().Be("TestRole");
        permissions[0].Source.Should().Be("direct");
    }

    [Fact]
    public async Task CheckUserPermission_WithValidPermission_ShouldReturnTrue()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");
        var roleId = await CreateTestRoleAsync(orgId, "TestRole");
        var permissionId = await CreateTestPermissionAsync("test:write");

        await LinkPermissionToRoleAsync(roleId, permissionId);
        await AssignUserRoleAsync(testUserId, roleId, orgId);

        // Act
        var hasPermissionSql = "SELECT authz.check_user_permission(@userId, @orgId, @permission, @audit);";
        var hasPermission = await DatabaseFixture.ExecuteScalarAsync<bool>(hasPermissionSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId),
            new NpgsqlParameter("@permission", "test:write"),
            new NpgsqlParameter("@audit", true));

        // Assert
        hasPermission.Should().BeTrue("User should have the assigned permission");
    }

    [Fact]
    public async Task CheckUserPermission_WithInvalidPermission_ShouldReturnFalse()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");
        var roleId = await CreateTestRoleAsync(orgId, "TestRole");
        var permissionId = await CreateTestPermissionAsync("test:read");

        await LinkPermissionToRoleAsync(roleId, permissionId);
        await AssignUserRoleAsync(testUserId, roleId, orgId);

        // Act
        var hasPermissionSql = "SELECT authz.check_user_permission(@userId, @orgId, @permission, @audit);";
        var hasPermission = await DatabaseFixture.ExecuteScalarAsync<bool>(hasPermissionSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId),
            new NpgsqlParameter("@permission", "test:write"), // Different permission
            new NpgsqlParameter("@audit", true));

        // Assert
        hasPermission.Should().BeFalse("User should not have unassigned permission");
    }

    [Fact]
    public async Task ResolveUserPermissions_WithInheritableRole_ShouldInheritFromParent()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (parentOrgId, _, _) = await CreateTestOrganizationAsync("ParentOrg");
        var (childOrgId, _, _) = await CreateTestOrganizationAsync("ChildOrg", parentId: parentOrgId);
        
        var inheritableRoleId = await CreateTestRoleAsync(parentOrgId, "InheritableRole", isInheritable: true);
        var permissionId = await CreateTestPermissionAsync("inherited:read");

        await LinkPermissionToRoleAsync(inheritableRoleId, permissionId);
        await AssignUserRoleAsync(testUserId, inheritableRoleId, parentOrgId);

        // Act - Check permissions in child organization
        var permissionsSql = """
            SELECT permission, role_name, permission_source
            FROM authz.resolve_user_permissions(@userId, @orgId, true, NULL);
            """;

        var permissions = new List<(string Permission, string RoleName, string Source)>();
        
        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(permissionsSql, connection);
        command.Parameters.AddWithValue("@userId", testUserId);
        command.Parameters.AddWithValue("@orgId", childOrgId); // Child org
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            permissions.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)
            ));
        }

        // Assert
        permissions.Should().HaveCount(1, "User should inherit permission in child organization");
        permissions[0].Permission.Should().Be("inherited:read");
        permissions[0].RoleName.Should().Be("InheritableRole");
        permissions[0].Source.Should().Be("inherited");
    }

    [Fact]
    public async Task ResolveUserPermissions_WithNonInheritableRole_ShouldNotInheritFromParent()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (parentOrgId, _, _) = await CreateTestOrganizationAsync("ParentOrg");
        var (childOrgId, _, _) = await CreateTestOrganizationAsync("ChildOrg", parentId: parentOrgId);
        
        var nonInheritableRoleId = await CreateTestRoleAsync(parentOrgId, "NonInheritableRole", isInheritable: false);
        var permissionId = await CreateTestPermissionAsync("noninherted:read");

        await LinkPermissionToRoleAsync(nonInheritableRoleId, permissionId);
        await AssignUserRoleAsync(testUserId, nonInheritableRoleId, parentOrgId);

        // Act - Check permissions in child organization
        var permissionsSql = """
            SELECT COUNT(*) 
            FROM authz.resolve_user_permissions(@userId, @orgId, true, NULL);
            """;

        var permissionCount = await DatabaseFixture.ExecuteScalarAsync<long>(permissionsSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", childOrgId));

        // Assert
        permissionCount.Should().Be(0, "User should not inherit non-inheritable permissions");
    }

    [Fact]
    public async Task CheckUserPermissionsBulk_WithMultiplePermissions_ShouldReturnCorrectResults()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");
        var roleId = await CreateTestRoleAsync(orgId, "TestRole");
        
        var permission1Id = await CreateTestPermissionAsync("test:read");
        var permission2Id = await CreateTestPermissionAsync("test:write");
        
        await LinkPermissionToRoleAsync(roleId, permission1Id);
        await LinkPermissionToRoleAsync(roleId, permission2Id);
        await AssignUserRoleAsync(testUserId, roleId, orgId);

        // Act
        var bulkCheckSql = """
            SELECT permission, has_permission 
            FROM authz.check_user_permissions_bulk(@userId, @orgId, @permissions, @audit);
            """;

        var results = new Dictionary<string, bool>();
        
        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(bulkCheckSql, connection);
        command.Parameters.AddWithValue("@userId", testUserId);
        command.Parameters.AddWithValue("@orgId", orgId);
        command.Parameters.Add(new NpgsqlParameter("@permissions", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = new[] { "test:read", "test:write", "test:delete" }
        });
        command.Parameters.AddWithValue("@audit", true);
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results[reader.GetString(0)] = reader.GetBoolean(1);
        }

        // Assert
        results.Should().HaveCount(3, "Should return results for all requested permissions");
        results["test:read"].Should().BeTrue("User should have read permission");
        results["test:write"].Should().BeTrue("User should have write permission");
        results["test:delete"].Should().BeFalse("User should not have delete permission");
    }

    [Fact]
    public async Task PermissionCheck_ShouldGenerateAuditEvent()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");

        // Act
        var hasPermissionSql = "SELECT authz.check_user_permission(@userId, @orgId, @permission, @audit);";
        await DatabaseFixture.ExecuteScalarAsync<bool>(hasPermissionSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId),
            new NpgsqlParameter("@permission", "test:audit"),
            new NpgsqlParameter("@audit", true));

        // Assert - Check that audit event was created
        var auditCheckSql = """
            SELECT COUNT(*) 
            FROM authz.audit_events 
            WHERE event_type = 'PERMISSION_CHECK' 
              AND user_id = @userId 
              AND organization_id = @orgId;
            """;

        var auditEventCount = await DatabaseFixture.ExecuteScalarAsync<long>(auditCheckSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId));

        auditEventCount.Should().BeGreaterThan(0, "Permission check should generate audit event");
    }

    // Helper methods
    private async Task LinkPermissionToRoleAsync(Guid roleId, Guid permissionId)
    {
        var sql = """
            INSERT INTO authz.role_permissions (role_id, permission_id, metadata)
            VALUES (@roleId, @permissionId, @metadata);
            """;

        await DatabaseFixture.ExecuteNonQueryAsync(sql,
            new NpgsqlParameter("@roleId", roleId),
            new NpgsqlParameter("@permissionId", permissionId),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" });
    }

    private async Task AssignUserRoleAsync(Guid userId, Guid roleId, Guid organizationId)
    {
        var sql = """
            INSERT INTO authz.user_roles (user_id, role_id, organization_id, metadata)
            VALUES (@userId, @roleId, @organizationId, @metadata);
            """;

        await DatabaseFixture.ExecuteNonQueryAsync(sql,
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@roleId", roleId),
            new NpgsqlParameter("@organizationId", organizationId),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" });
    }
}