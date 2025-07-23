using MassTransit;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Dapper;
using System.Data;

namespace Nyssa.Mcp.Server.Services.RbacMessageHandlers
{
    /// <summary>
    /// Message handler for audit logging operations
    /// </summary>
    public class AuditLoggingHandler : IConsumer<LogAuthenticationEventRequest>
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly ILogger<AuditLoggingHandler> _logger;

        public AuditLoggingHandler(
            IDatabaseConnectionFactory connectionFactory,
            ILogger<AuditLoggingHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <summary>
        /// Handles audit event logging requests
        /// </summary>
        public async Task Consume(ConsumeContext<LogAuthenticationEventRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing audit event logging request: {EventType} for user: {UserId}", 
                request.EventType, request.UserId);

            try
            {
                var logResult = await LogAuditEventAsync(request);
                
                if (logResult.Success)
                {
                    _logger.LogInformation("Audit event logged successfully: {EventType} for user {UserId}", 
                        request.EventType, request.UserId);
                }
                else
                {
                    _logger.LogError("Failed to log audit event {EventType} for user {UserId}: {Errors}", 
                        request.EventType, request.UserId, 
                        string.Join(", ", logResult.Errors.Select(e => e.Text)));
                    
                    // Don't throw here - audit logging failures shouldn't break the main flow
                    // Just log the error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log audit event {EventType} for user {UserId}: {Error}", 
                    request.EventType, request.UserId, ex.Message);
                
                // Don't throw here - audit logging failures shouldn't break the main flow
            }
        }

        /// <summary>
        /// Logs an audit event using the database function
        /// </summary>
        private async Task<Result> LogAuditEventAsync(LogAuthenticationEventRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                const string query = @"
                    SELECT authz.log_audit_event(
                        @UserId::uuid, 
                        @EventType, 
                        @Details, 
                        @Category, 
                        @Source, 
                        @IpAddress
                    ) as event_id";

                var eventId = await connection.QuerySingleAsync<string>(query, new
                {
                    UserId = string.IsNullOrEmpty(request.UserId) ? (object)DBNull.Value : request.UserId,
                    EventType = request.EventType,
                    Details = request.Details ?? "{}",
                    Category = request.Category ?? "authentication",
                    Source = request.Source ?? "mcp_server",
                    IpAddress = request.IpAddress
                });

                if (string.IsNullOrEmpty(eventId))
                {
                    return Result.Fail(RbacErrors.Database.CustomQueryError("log_audit_event", "Failed to create audit log entry"));
                }

                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error logging audit event {EventType}: {Error}", 
                    request.EventType, ex.Message);
                return Result.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }
    }
}