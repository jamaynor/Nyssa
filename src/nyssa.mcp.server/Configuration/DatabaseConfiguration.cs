using Npgsql;
using System.Data;
using Nyssa.Mcp.Server.Models;
using Microsoft.Extensions.Options;

namespace Nyssa.Mcp.Server.Configuration
{
    /// <summary>
    /// Configuration options for PostgreSQL RBAC database
    /// </summary>
    public class DatabaseOptions
    {
        public const string SectionName = "Database";

        /// <summary>
        /// PostgreSQL connection string
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Connection pool settings
        /// </summary>
        public ConnectionPoolSettings Pool { get; set; } = new();

        /// <summary>
        /// Query timeout settings
        /// </summary>
        public TimeoutSettings Timeout { get; set; } = new();

        /// <summary>
        /// Health check settings
        /// </summary>
        public HealthCheckSettings HealthCheck { get; set; } = new();
    }

    public class ConnectionPoolSettings
    {
        public int MinPoolSize { get; set; } = 5;
        public int MaxPoolSize { get; set; } = 50;
        public TimeSpan ConnectionIdleLifetime { get; set; } = TimeSpan.FromMinutes(15);
        public TimeSpan ConnectionPruningInterval { get; set; } = TimeSpan.FromMinutes(10);
    }

    public class TimeoutSettings
    {
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(15);
    }

    public class HealthCheckSettings
    {
        public bool Enabled { get; set; } = true;
        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
        public string HealthCheckQuery { get; set; } = "SELECT 1";
    }

    /// <summary>
    /// Database connection factory that returns Result-wrapped connections
    /// </summary>
    public interface IDatabaseConnectionFactory
    {
        Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default);
        Task<Result<T>> ExecuteWithConnectionAsync<T>(
            Func<IDbConnection, Task<T>> operation, 
            CancellationToken cancellationToken = default);
        Task<Result> ExecuteWithConnectionAsync(
            Func<IDbConnection, Task> operation, 
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// PostgreSQL implementation of the database connection factory
    /// </summary>
    public class PostgreSqlConnectionFactory : IDatabaseConnectionFactory
    {
        private readonly DatabaseOptions _options;
        private readonly ILogger<PostgreSqlConnectionFactory> _logger;

        public PostgreSqlConnectionFactory(
            IOptions<DatabaseOptions> options,
            ILogger<PostgreSqlConnectionFactory> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(_options.ConnectionString))
                {
                    return RbacErrors.Database.CustomQueryError(
                        "create_connection", 
                        "Database connection string is not configured");
                }

                var connectionString = BuildConnectionString();
                var connection = new NpgsqlConnection(connectionString);
                
                await connection.OpenAsync(cancellationToken);
                
                _logger.LogDebug("Database connection opened successfully");
                return Result<IDbConnection>.Ok(connection);
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError(ex, "Failed to create database connection: {Error}", ex.Message);
                return RbacErrors.Database.ConnectionFailed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating database connection: {Error}", ex.Message);
                return RbacErrors.Database.CustomQueryError("create_connection", ex.Message);
            }
        }

        public async Task<Result<T>> ExecuteWithConnectionAsync<T>(
            Func<IDbConnection, Task<T>> operation, 
            CancellationToken cancellationToken = default)
        {
            var connectionResult = await CreateConnectionAsync(cancellationToken);
            if (!connectionResult.Success)
                return Result<T>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                var result = await operation(connection);
                return Result<T>.Ok(result);
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError(ex, "Database operation failed: {Error}", ex.Message);
                
                return ex.SqlState switch
                {
                    "23505" => RbacErrors.Database.ConstraintViolation, // Unique violation
                    "23503" => RbacErrors.Database.ConstraintViolation, // Foreign key violation
                    "23514" => RbacErrors.Database.ConstraintViolation, // Check violation
                    _ => RbacErrors.Database.QueryExecutionFailed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during database operation: {Error}", ex.Message);
                return RbacErrors.Database.CustomQueryError("execute_operation", ex.Message);
            }
        }

        public async Task<Result> ExecuteWithConnectionAsync(
            Func<IDbConnection, Task> operation, 
            CancellationToken cancellationToken = default)
        {
            var connectionResult = await CreateConnectionAsync(cancellationToken);
            if (!connectionResult.Success)
                return Result.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                await operation(connection);
                return Result.Ok();
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError(ex, "Database operation failed: {Error}", ex.Message);
                
                return ex.SqlState switch
                {
                    "23505" => RbacErrors.Database.ConstraintViolation, // Unique violation
                    "23503" => RbacErrors.Database.ConstraintViolation, // Foreign key violation
                    "23514" => RbacErrors.Database.ConstraintViolation, // Check violation
                    _ => RbacErrors.Database.QueryExecutionFailed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during database operation: {Error}", ex.Message);
                return RbacErrors.Database.CustomQueryError("execute_operation", ex.Message);
            }
        }

        private string BuildConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_options.ConnectionString)
            {
                MinPoolSize = _options.Pool.MinPoolSize,
                MaxPoolSize = _options.Pool.MaxPoolSize,
                ConnectionIdleLifetime = (int)_options.Pool.ConnectionIdleLifetime.TotalSeconds,
                ConnectionPruningInterval = (int)_options.Pool.ConnectionPruningInterval.TotalSeconds,
                CommandTimeout = (int)_options.Timeout.CommandTimeout.TotalSeconds,
                Timeout = (int)_options.Timeout.ConnectionTimeout.TotalSeconds
            };

            return builder.ToString();
        }
    }

    /// <summary>
    /// Extension methods for database configuration
    /// </summary>
    public static class DatabaseConfigurationExtensions
    {
        /// <summary>
        /// Configures PostgreSQL database services for RBAC operations
        /// </summary>
        public static IServiceCollection AddRbacDatabase(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
            services.AddSingleton<IDatabaseConnectionFactory, PostgreSqlConnectionFactory>();

            // Health checks will be added in Phase 3 when we implement monitoring

            return services;
        }
    }
}