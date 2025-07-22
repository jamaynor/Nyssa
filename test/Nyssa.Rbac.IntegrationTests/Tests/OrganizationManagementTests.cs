using FluentAssertions;
using Npgsql;
using Nyssa.Rbac.IntegrationTests.Setup;

namespace Nyssa.Rbac.IntegrationTests.Tests;

/// <summary>
/// Integration tests for organization management functionality
/// </summary>
public class OrganizationManagementTests : RbacTestBase
{
    public OrganizationManagementTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task CreateOrganization_WithValidData_ShouldSucceed()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();

        // Act
        var (orgId, orgName, orgPath) = await CreateTestOrganizationAsync(
            "TestOrg", "Test Organization", "A test organization", null, testUserId);

        // Assert
        orgId.Should().NotBe(Guid.Empty);
        orgName.Should().Be("TestOrg");
        orgPath.Should().Be("admin.testorg");

        await AssertOrganizationExistsAsync(orgId, "TestOrg", "admin.testorg");
    }

    [Fact]
    public async Task CreateOrganization_WithDuplicateName_ShouldFail()
    {
        // Arrange
        await CreateTestOrganizationAsync("DuplicateOrg");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => 
            CreateTestOrganizationAsync("DuplicateOrg"));

        exception.Message.Should().Contain("already exists", "Duplicate organization should be rejected");
    }

    [Fact]
    public async Task MoveOrganization_ToNewParent_ShouldUpdatePathAndDescendants()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (sourceParentId, _, _) = await CreateTestOrganizationAsync("SourceParent");
        var (targetParentId, _, _) = await CreateTestOrganizationAsync("TargetParent");
        var (orgToMoveId, _, _) = await CreateTestOrganizationAsync("OrgToMove", parentId: sourceParentId);
        var (childOrgId, _, _) = await CreateTestOrganizationAsync("ChildOrg", parentId: orgToMoveId);

        // Act
        var moveSql = "SELECT authz.move_organization(@orgId, @newParentId, @movedBy);";
        var moveResult = await DatabaseFixture.ExecuteScalarAsync<bool>(moveSql,
            new NpgsqlParameter("@orgId", orgToMoveId),
            new NpgsqlParameter("@newParentId", targetParentId),
            new NpgsqlParameter("@movedBy", testUserId));

        // Assert
        moveResult.Should().BeTrue("Move operation should succeed");

        // Check that the moved organization has the correct new path
        var pathCheckSql = "SELECT path::text FROM authz.organizations WHERE id = @orgId;";
        var newPath = await DatabaseFixture.ExecuteScalarAsync<string>(pathCheckSql,
            new NpgsqlParameter("@orgId", orgToMoveId));
        newPath.Should().Be("admin.targetparent.orgtomove");

        // Check that the child organization's path was also updated
        var childPath = await DatabaseFixture.ExecuteScalarAsync<string>(pathCheckSql,
            new NpgsqlParameter("@orgId", childOrgId));
        childPath.Should().Be("admin.targetparent.orgtomove.childorg");
    }

    [Fact]
    public async Task MoveOrganization_ToDescendant_ShouldFail()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (parentOrgId, _, _) = await CreateTestOrganizationAsync("Parent");
        var (childOrgId, _, _) = await CreateTestOrganizationAsync("Child", parentId: parentOrgId);

        // Act & Assert - Try to move parent under child (circular reference)
        var moveSql = "SELECT authz.move_organization(@orgId, @newParentId, @movedBy);";
        
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            DatabaseFixture.ExecuteScalarAsync<bool>(moveSql,
                new NpgsqlParameter("@orgId", parentOrgId),
                new NpgsqlParameter("@newParentId", childOrgId),
                new NpgsqlParameter("@movedBy", testUserId)));

        exception.Message.Should().Contain("descendant", "Should prevent circular references");
    }

    [Fact]
    public async Task GetOrganizationHierarchy_WithUserFilter_ShouldReturnAccessibleOrganizations()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (org1Id, _, _) = await CreateTestOrganizationAsync("AccessibleOrg");
        var (org2Id, _, _) = await CreateTestOrganizationAsync("InaccessibleOrg");

        // Add user as member of org1 only
        var membershipSql = """
            INSERT INTO authz.organization_memberships (user_id, organization_id, status, metadata)
            VALUES (@userId, @orgId, 'active', @metadata);
            """;
        
        await DatabaseFixture.ExecuteNonQueryAsync(membershipSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", org1Id),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" });

        // Act
        var hierarchySql = """
            SELECT name, has_access, is_direct_member 
            FROM authz.get_organization_hierarchy(@userId, NULL, NULL, false)
            WHERE name IN ('AccessibleOrg', 'InaccessibleOrg');
            """;

        var results = new List<(string Name, bool HasAccess, bool IsDirectMember)>();
        
        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(hierarchySql, connection);
        command.Parameters.AddWithValue("@userId", testUserId);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2)
            ));
        }

        // Assert
        var accessibleOrg = results.FirstOrDefault(r => r.Name == "AccessibleOrg");
        accessibleOrg.HasAccess.Should().BeTrue("User should have access to organization they're a member of");
        accessibleOrg.IsDirectMember.Should().BeTrue("User should be marked as direct member");

        var inaccessibleOrg = results.FirstOrDefault(r => r.Name == "InaccessibleOrg");
        inaccessibleOrg.HasAccess.Should().BeFalse("User should not have access to organization they're not a member of");
        inaccessibleOrg.IsDirectMember.Should().BeFalse("User should not be marked as direct member");
    }

    [Fact]
    public async Task UserHasOrganizationAccess_WithMembership_ShouldReturnTrue()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");

        // Add user as member
        var membershipSql = """
            INSERT INTO authz.organization_memberships (user_id, organization_id, status, metadata)
            VALUES (@userId, @orgId, 'active', @metadata);
            """;
        
        await DatabaseFixture.ExecuteNonQueryAsync(membershipSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" });

        // Act
        var accessCheckSql = "SELECT authz.user_has_organization_access(@userId, @orgId);";
        var hasAccess = await DatabaseFixture.ExecuteScalarAsync<bool>(accessCheckSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId));

        // Assert
        hasAccess.Should().BeTrue("User should have access to organization they're a member of");
    }

    [Fact]
    public async Task UserHasOrganizationAccess_WithoutMembership_ShouldReturnFalse()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();
        var (orgId, _, _) = await CreateTestOrganizationAsync("TestOrg");

        // Act
        var accessCheckSql = "SELECT authz.user_has_organization_access(@userId, @orgId);";
        var hasAccess = await DatabaseFixture.ExecuteScalarAsync<bool>(accessCheckSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId));

        // Assert
        hasAccess.Should().BeFalse("User should not have access to organization they're not a member of");
    }

    [Fact]
    public async Task CreateOrganization_ShouldGenerateAuditEvent()
    {
        // Arrange
        var testUserId = await CreateTestUserAsync();

        // Act
        var (orgId, _, _) = await CreateTestOrganizationAsync("AuditTestOrg", createdBy: testUserId);

        // Assert - Check that audit event was created
        var auditCheckSql = """
            SELECT COUNT(*) 
            FROM authz.audit_events 
            WHERE event_type = 'ORGANIZATION_CREATED' 
              AND user_id = @userId 
              AND resource_id = @orgId;
            """;

        var auditEventCount = await DatabaseFixture.ExecuteScalarAsync<long>(auditCheckSql,
            new NpgsqlParameter("@userId", testUserId),
            new NpgsqlParameter("@orgId", orgId));

        auditEventCount.Should().Be(1, "Organization creation should generate audit event");
    }
}