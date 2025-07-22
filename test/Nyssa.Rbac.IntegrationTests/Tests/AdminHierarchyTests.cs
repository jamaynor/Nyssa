using FluentAssertions;
using Npgsql;
using Nyssa.Rbac.IntegrationTests.Setup;

namespace Nyssa.Rbac.IntegrationTests.Tests;

/// <summary>
/// Integration tests for Admin organization hierarchy enforcement
/// </summary>
public class AdminHierarchyTests : RbacTestBase
{
    public AdminHierarchyTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task AdminOrganization_ShouldExist_WithCorrectProperties()
    {
        // Act & Assert
        await AssertOrganizationExistsAsync(AdminOrganizationId, "Admin", "admin");

        // Additional assertions for Admin-specific properties
        var sql = """
            SELECT metadata, parent_id 
            FROM authz.organizations 
            WHERE id = @adminId;
            """;

        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@adminId", AdminOrganizationId);
        using var reader = await command.ExecuteReaderAsync();

        reader.Read().Should().BeTrue("Admin organization should exist");
        reader.IsDBNull(1).Should().BeTrue("Admin should have no parent (NULL parent_id)");
        
        var metadata = reader.GetString(0);
        metadata.Should().Contain("\"is_root\": true");
        metadata.Should().Contain("\"is_system_org\": true");
    }

    [Fact]
    public async Task CreateOrganization_WithoutParent_ShouldAutomaticallyAssignAdminAsParent()
    {
        // Act
        var (orgId, orgName, orgPath) = await CreateTestOrganizationAsync("TestCorp", "Test Corporation");

        // Assert
        orgName.Should().Be("TestCorp");
        orgPath.Should().Be("admin.testcorp", "Organization should be created under Admin");

        // Verify parent relationship
        var parentCheckSql = "SELECT parent_id FROM authz.organizations WHERE id = @orgId;";
        var parentId = await DatabaseFixture.ExecuteScalarAsync<Guid>(parentCheckSql, 
            new NpgsqlParameter("@orgId", orgId));

        parentId.Should().Be(AdminOrganizationId, "Organization should have Admin as parent");
    }

    [Fact]
    public async Task CreateOrganization_WithSpecificParent_ShouldRespectParentHierarchy()
    {
        // Arrange
        var (parentOrgId, _, _) = await CreateTestOrganizationAsync("ParentCorp");

        // Act
        var (childOrgId, childName, childPath) = await CreateTestOrganizationAsync(
            "ChildCorp", "Child Corporation", null, parentOrgId);

        // Assert
        childName.Should().Be("ChildCorp");
        childPath.Should().Be("admin.parentcorp.childcorp", 
            "Child organization should be under parent in Admin hierarchy");

        // Verify parent relationship
        var parentCheckSql = "SELECT parent_id FROM authz.organizations WHERE id = @orgId;";
        var actualParentId = await DatabaseFixture.ExecuteScalarAsync<Guid>(parentCheckSql,
            new NpgsqlParameter("@orgId", childOrgId));

        actualParentId.Should().Be(parentOrgId, "Child should have specified parent");
    }

    [Fact]
    public async Task AllOrganizations_ExceptAdmin_ShouldDescendFromAdmin()
    {
        // Arrange
        await CreateTestOrganizationAsync("Org1");
        await CreateTestOrganizationAsync("Org2");
        var (org3Id, _, _) = await CreateTestOrganizationAsync("Org3");
        await CreateTestOrganizationAsync("Org4", parentId: org3Id); // Child of Org3

        // Act
        var nonCompliantSql = """
            SELECT COUNT(*) 
            FROM authz.organizations 
            WHERE id <> @adminId 
              AND is_active = true 
              AND NOT path::text LIKE 'admin%';
            """;

        var nonCompliantCount = await DatabaseFixture.ExecuteScalarAsync<long>(nonCompliantSql,
            new NpgsqlParameter("@adminId", AdminOrganizationId));

        // Assert
        nonCompliantCount.Should().Be(0, 
            "All organizations except Admin should have paths starting with 'admin'");
    }

    [Fact]
    public async Task GetOrganizationHierarchy_ShouldShowCorrectAdminTree()
    {
        // Arrange
        var (corp1Id, _, _) = await CreateTestOrganizationAsync("Corp1");
        var (corp2Id, _, _) = await CreateTestOrganizationAsync("Corp2");
        await CreateTestOrganizationAsync("Dept1", parentId: corp1Id);
        await CreateTestOrganizationAsync("Dept2", parentId: corp2Id);

        // Act
        var hierarchySql = """
            SELECT name, path::text, parent_id, 
                   CASE WHEN parent_id IS NULL THEN 'ROOT'
                        WHEN parent_id = @adminId THEN 'ADMIN_CHILD'
                        ELSE 'DESCENDANT' END as level_type
            FROM authz.organizations 
            WHERE is_active = true 
            ORDER BY path;
            """;

        var organizations = new List<(string Name, string Path, Guid? ParentId, string LevelType)>();

        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(hierarchySql, connection);
        command.Parameters.AddWithValue("@adminId", AdminOrganizationId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var parentId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            organizations.Add((
                reader.GetString(0),
                reader.GetString(1), 
                parentId,
                reader.GetString(3)
            ));
        }

        // Assert
        organizations.Should().HaveCountGreaterThan(4, "Should have Admin + test organizations");
        
        var admin = organizations.First(o => o.Path == "admin");
        admin.LevelType.Should().Be("ROOT");
        admin.ParentId.Should().BeNull();

        var adminChildren = organizations.Where(o => o.LevelType == "ADMIN_CHILD").ToList();
        adminChildren.Should().HaveCount(2, "Corp1 and Corp2 should be direct children of Admin");
        adminChildren.Should().OnlyContain(o => o.ParentId == AdminOrganizationId);

        var descendants = organizations.Where(o => o.LevelType == "DESCENDANT").ToList();
        descendants.Should().HaveCount(2, "Dept1 and Dept2 should be descendants");
        descendants.Should().OnlyContain(o => o.Path.StartsWith("admin."));
    }

    [Fact]
    public async Task EnsureAdminOrganization_ShouldBeIdempotent()
    {
        // Arrange - Count current admin organizations
        var countBeforeSql = "SELECT COUNT(*) FROM authz.organizations WHERE name = 'Admin';";
        var countBefore = await DatabaseFixture.ExecuteScalarAsync<long>(countBeforeSql);

        // Act - Call ensure admin multiple times
        var ensureSql = "SELECT authz.ensure_admin_organization();";
        await DatabaseFixture.ExecuteScalarAsync<Guid>(ensureSql);
        await DatabaseFixture.ExecuteScalarAsync<Guid>(ensureSql);
        await DatabaseFixture.ExecuteScalarAsync<Guid>(ensureSql);

        // Assert - Count should remain the same
        var countAfter = await DatabaseFixture.ExecuteScalarAsync<long>(countBeforeSql);
        countAfter.Should().Be(countBefore, "ensure_admin_organization should be idempotent");
        countAfter.Should().Be(1, "There should be exactly one Admin organization");
    }

    [Fact]
    public async Task AdminOrganization_ShouldHaveCorrectUuid()
    {
        // Act
        var adminIdSql = "SELECT id FROM authz.organizations WHERE name = 'Admin';";
        var actualAdminId = await DatabaseFixture.ExecuteScalarAsync<Guid>(adminIdSql);

        // Assert
        actualAdminId.Should().Be(AdminOrganizationId, 
            "Admin organization should have the well-known UUID");
    }
}