using FluentAssertions;
using Npgsql;
using Nyssa.Rbac.IntegrationTests.Setup;

namespace Nyssa.Rbac.IntegrationTests.Setup;

/// <summary>
/// Base class for all RBAC integration tests
/// </summary>
[Collection("Database")]
public abstract class RbacTestBase : IAsyncLifetime
{
    protected readonly DatabaseFixture DatabaseFixture;
    
    // Well-known Admin organization ID
    protected static readonly Guid AdminOrganizationId = new("00000000-0000-0000-0000-000000000001");

    protected RbacTestBase(DatabaseFixture databaseFixture)
    {
        DatabaseFixture = databaseFixture;
    }

    public virtual async Task InitializeAsync()
    {
        await DatabaseFixture.CleanupTestDataAsync();
    }

    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Create a test user with the specified email
    /// </summary>
    protected async Task<Guid> CreateTestUserAsync(string email = "test@example.com", string? firstName = "Test", string? lastName = "User")
    {
        var sql = """
            INSERT INTO authz.users (email, first_name, last_name, metadata)
            VALUES (@email, @firstName, @lastName, @metadata)
            RETURNING id;
            """;

        var parameters = new[]
        {
            new NpgsqlParameter("@email", email),
            new NpgsqlParameter("@firstName", firstName ?? (object)DBNull.Value),
            new NpgsqlParameter("@lastName", lastName ?? (object)DBNull.Value),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" }
        };

        var userId = await DatabaseFixture.ExecuteScalarAsync<Guid>(sql, parameters);
        return userId;
    }

    /// <summary>
    /// Create a test organization
    /// </summary>
    protected async Task<(Guid Id, string Name, string Path)> CreateTestOrganizationAsync(
        string name, 
        string? displayName = null, 
        string? description = null, 
        Guid? parentId = null,
        Guid? createdBy = null)
    {
        var sql = """
            SELECT id, name, path::text FROM authz.create_organization(
                @name, @displayName, @description, @parentId, @createdBy, @metadata
            );
            """;

        var parameters = new[]
        {
            new NpgsqlParameter("@name", name),
            new NpgsqlParameter("@displayName", displayName ?? (object)DBNull.Value),
            new NpgsqlParameter("@description", description ?? (object)DBNull.Value),
            new NpgsqlParameter("@parentId", parentId ?? (object)DBNull.Value),
            new NpgsqlParameter("@createdBy", createdBy ?? (object)DBNull.Value),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" }
        };

        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
        }
        
        throw new InvalidOperationException("Failed to create test organization");
    }

    /// <summary>
    /// Create a test role
    /// </summary>
    protected async Task<Guid> CreateTestRoleAsync(
        Guid organizationId,
        string name,
        string? displayName = null,
        string? description = null,
        bool isInheritable = true)
    {
        var sql = """
            INSERT INTO authz.roles (organization_id, name, display_name, description, is_inheritable, metadata)
            VALUES (@organizationId, @name, @displayName, @description, @isInheritable, @metadata)
            RETURNING id;
            """;

        var parameters = new[]
        {
            new NpgsqlParameter("@organizationId", organizationId),
            new NpgsqlParameter("@name", name),
            new NpgsqlParameter("@displayName", displayName ?? (object)DBNull.Value),
            new NpgsqlParameter("@description", description ?? (object)DBNull.Value),
            new NpgsqlParameter("@isInheritable", isInheritable),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" }
        };

        var roleId = await DatabaseFixture.ExecuteScalarAsync<Guid>(sql, parameters);
        return roleId;
    }

    /// <summary>
    /// Create a test permission
    /// </summary>
    protected async Task<Guid> CreateTestPermissionAsync(
        string permission,
        string category = "test",
        string? displayName = null,
        string? description = null)
    {
        var sql = """
            INSERT INTO authz.permissions (permission, display_name, description, category, metadata)
            VALUES (@permission, @displayName, @description, @category, @metadata)
            RETURNING id;
            """;

        var parameters = new[]
        {
            new NpgsqlParameter("@permission", permission),
            new NpgsqlParameter("@displayName", displayName ?? (object)DBNull.Value),
            new NpgsqlParameter("@description", description ?? (object)DBNull.Value),
            new NpgsqlParameter("@category", category),
            new NpgsqlParameter("@metadata", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = """{"test_context": true}""" }
        };

        var permissionId = await DatabaseFixture.ExecuteScalarAsync<Guid>(sql, parameters);
        return permissionId;
    }

    /// <summary>
    /// Verify that an organization exists with the expected properties
    /// </summary>
    protected async Task AssertOrganizationExistsAsync(Guid organizationId, string? expectedName = null, string? expectedPath = null)
    {
        var sql = """
            SELECT name, path::text, parent_id, is_active 
            FROM authz.organizations 
            WHERE id = @id;
            """;

        using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", organizationId);
        using var reader = await command.ExecuteReaderAsync();

        reader.Read().Should().BeTrue($"Organization with ID {organizationId} should exist");
        
        if (expectedName != null)
        {
            reader.GetString(0).Should().Be(expectedName);
        }
        
        if (expectedPath != null)
        {
            reader.GetString(1).Should().Be(expectedPath);
        }
        
        reader.GetBoolean(3).Should().BeTrue("Organization should be active");
    }
}