using Npgsql;
using System.Text;

namespace Nyssa.Rbac.IntegrationTests.Setup;

/// <summary>
/// XUnit collection fixture for managing PostgreSQL test database lifecycle
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    // Use existing PostgreSQL instance instead of TestContainers
    public string ConnectionString => "Host=localhost;Database=rbac_test;Username=postgres;Password=postgres;Include Error Detail=true";
    private string AdminConnectionString => "Host=localhost;Database=postgres;Username=postgres;Password=postgres";
    
    public DatabaseFixture()
    {
        // No container initialization needed - using existing PostgreSQL
    }

    public async Task InitializeAsync()
    {
        await CreateTestDatabaseIfNotExists();
        await InitializeDatabaseSchema();
    }

    public async Task DisposeAsync()
    {
        // No container cleanup needed
        await Task.CompletedTask;
    }

    private async Task CreateTestDatabaseIfNotExists()
    {
        try
        {
            using var adminConnection = new NpgsqlConnection(AdminConnectionString);
            await adminConnection.OpenAsync();
            
            // Check if database exists
            var checkSql = "SELECT 1 FROM pg_database WHERE datname = 'rbac_test';";
            using var checkCommand = new NpgsqlCommand(checkSql, adminConnection);
            var exists = await checkCommand.ExecuteScalarAsync();
            
            if (exists == null)
            {
                // Create database
                var createSql = "CREATE DATABASE rbac_test;";
                using var createCommand = new NpgsqlCommand(createSql, adminConnection);
                await createCommand.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create test database: {ex.Message}", ex);
        }
    }

    private async Task InitializeDatabaseSchema()
    {
        var baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\..\..\database\authorization"));
        Console.WriteLine($"Base database directory: {baseDir}");
        
        var schemaFiles = new[]
        {
            Path.Combine(baseDir, @"schema\01_create_schema.sql"),
            Path.Combine(baseDir, @"tables\01_organizations.sql"), 
            Path.Combine(baseDir, @"tables\02_users.sql"),
            Path.Combine(baseDir, @"tables\03_organization_memberships.sql"),
            Path.Combine(baseDir, @"tables\04_roles.sql"),
            Path.Combine(baseDir, @"tables\05_permissions.sql"),
            Path.Combine(baseDir, @"tables\06_role_permissions.sql"),
            Path.Combine(baseDir, @"tables\07_user_roles.sql"),
            Path.Combine(baseDir, @"tables\08_token_blacklist.sql"),
            Path.Combine(baseDir, @"tables\09_audit_events.sql"),
            Path.Combine(baseDir, @"functions\01_organization_management.sql"),
            Path.Combine(baseDir, @"functions\02_permission_resolution.sql"),
            Path.Combine(baseDir, @"functions\03_role_management.sql"),
            Path.Combine(baseDir, @"functions\04_token_audit_management.sql"),
            Path.Combine(baseDir, @"views\01_user_effective_permissions.sql")
        };

        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Clean up existing schema first for test isolation
        var cleanupSql = """
            DROP SCHEMA IF EXISTS authz CASCADE;
            """;
        Console.WriteLine("Cleaning up existing schema...");
        using var cleanupCommand = new NpgsqlCommand(cleanupSql, connection);
        await cleanupCommand.ExecuteNonQueryAsync();

        // Enable required PostgreSQL extensions (after cleanup)
        var extensions = new[]
        {
            "CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";",
            "CREATE EXTENSION IF NOT EXISTS ltree;", 
            "CREATE EXTENSION IF NOT EXISTS pgcrypto;",
            "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
            "CREATE EXTENSION IF NOT EXISTS btree_gist;"
        };

        foreach (var extensionSql in extensions)
        {
            Console.WriteLine($"Enabling extension: {extensionSql}");
            using var command = new NpgsqlCommand(extensionSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        // Create the authz schema if it doesn't exist yet (before processing schema files)
        using var preSchemaCommand = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS authz;", connection);
        await preSchemaCommand.ExecuteNonQueryAsync();

        // Create type aliases and function wrappers for extensions in the authz schema to match the function definitions
        var typeAliases = new[]
        {
            "CREATE DOMAIN authz.ltree AS ltree;",
            "CREATE DOMAIN authz.uuid AS uuid;"
        };

        var functionWrappers = new[]
        {
            "CREATE OR REPLACE FUNCTION authz.uuid_generate_v4() RETURNS UUID AS $$ SELECT public.uuid_generate_v4(); $$ LANGUAGE sql;",
            "CREATE OR REPLACE FUNCTION authz.uuid_generate_v1() RETURNS UUID AS $$ SELECT public.uuid_generate_v1(); $$ LANGUAGE sql;"
        };

        foreach (var typeAliasSql in typeAliases)
        {
            Console.WriteLine($"Creating type alias: {typeAliasSql}");
            using var command = new NpgsqlCommand(typeAliasSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var functionWrapperSql in functionWrappers)
        {
            Console.WriteLine($"Creating function wrapper: {functionWrapperSql}");
            using var command = new NpgsqlCommand(functionWrapperSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var schemaFile in schemaFiles)
        {
            Console.WriteLine($"Processing schema file: {schemaFile}");
            
            if (File.Exists(schemaFile))
            {
                var sql = await File.ReadAllTextAsync(schemaFile);
                Console.WriteLine($"Executing SQL from: {Path.GetFileName(schemaFile)}");
                using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                Console.WriteLine($"WARNING: Schema file not found: {schemaFile}");
            }
        }

        // Create current month partition for audit_events if it doesn't exist
        var currentDate = DateTime.Now;
        var partitionName = $"audit_events_y{currentDate:yyyy}m{currentDate:MM}";
        var startDate = new DateTime(currentDate.Year, currentDate.Month, 1);
        var endDate = startDate.AddMonths(1);
        
        var partitionSql = $"""
            CREATE TABLE IF NOT EXISTS authz.{partitionName} PARTITION OF authz.audit_events 
            FOR VALUES FROM ('{startDate:yyyy-MM-dd}') TO ('{endDate:yyyy-MM-dd}');
            """;
        
        Console.WriteLine($"Creating current month partition: {partitionName}");
        using var partitionCommand = new NpgsqlCommand(partitionSql, connection);
        await partitionCommand.ExecuteNonQueryAsync();

        // Create a temporary system user to satisfy foreign key constraints during admin org creation
        var systemUserSql = """
            INSERT INTO authz.users (id, email, first_name, last_name, metadata) 
            VALUES ('00000000-0000-0000-0000-000000000001', 'system@admin.local', 'System', 'Administrator', '{"is_system_user": true}')
            ON CONFLICT (id) DO NOTHING;
            """;
        using var systemUserCommand = new NpgsqlCommand(systemUserSql, connection);
        await systemUserCommand.ExecuteNonQueryAsync();

        // Ensure Admin organization exists
        var ensureAdminSql = "SELECT authz.ensure_admin_organization();";
        using var ensureAdminCommand = new NpgsqlCommand(ensureAdminSql, connection);
        await ensureAdminCommand.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Execute SQL command and return result
    /// </summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        
        // Handle array results for table-valued functions
        if (result is object[] array && array.Length > 0)
        {
            return (T?)array[0];
        }
        
        return (T?)result;
    }

    /// <summary>
    /// Execute SQL command without return value
    /// </summary>
    public async Task ExecuteNonQueryAsync(string sql, params NpgsqlParameter[] parameters)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Clean up test data between tests
    /// </summary>
    public async Task CleanupTestDataAsync()
    {
        var cleanupSql = """
            DELETE FROM authz.audit_events WHERE details->>'test_context' = 'true'
                OR organization_id IN (SELECT id FROM authz.organizations WHERE metadata->>'test_context' = 'true')
                OR user_id IN (SELECT id FROM authz.users WHERE metadata->>'test_context' = 'true');
            DELETE FROM authz.user_roles WHERE metadata->>'test_context' = 'true';
            DELETE FROM authz.organization_memberships WHERE metadata->>'test_context' = 'true';
            DELETE FROM authz.organizations WHERE metadata->>'test_context' = 'true';
            DELETE FROM authz.users WHERE metadata->>'test_context' = 'true';
            DELETE FROM authz.roles WHERE metadata->>'test_context' = 'true';
            DELETE FROM authz.permissions WHERE metadata->>'test_context' = 'true';
            """;
        
        await ExecuteNonQueryAsync(cleanupSql);
    }
}