using MassTransit;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authorization;
using Dapper;
using System.Data;

namespace Nyssa.Mcp.Server.Services.RbacMessageHandlers
{
    /// <summary>
    /// Message handler for token management operations
    /// </summary>
    public class TokenManagementHandler : 
        IConsumer<CheckTokenBlacklistRequest>,
        IConsumer<BlacklistTokenRequest>
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly ILogger<TokenManagementHandler> _logger;

        public TokenManagementHandler(
            IDatabaseConnectionFactory connectionFactory,
            ILogger<TokenManagementHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <summary>
        /// Handles token blacklist check requests
        /// </summary>
        public async Task Consume(ConsumeContext<CheckTokenBlacklistRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing token blacklist check for token: {TokenId}", request.TokenId);

            try
            {
                var checkResult = await CheckTokenBlacklistAsync(request);
                
                if (checkResult.Success)
                {
                    var response = checkResult.Value;
                    await context.RespondAsync(response);
                    _logger.LogInformation("Token blacklist check completed for {TokenId}: IsBlacklisted={IsBlacklisted}", 
                        request.TokenId, response.IsBlacklisted);
                }
                else
                {
                    _logger.LogError("Failed to check token blacklist for {TokenId}: {Errors}", 
                        request.TokenId, string.Join(", ", checkResult.Errors.Select(e => e.Text)));
                    throw new InvalidOperationException($"Failed to check token blacklist: {string.Join(", ", checkResult.Errors.Select(e => e.Text))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check token blacklist for {TokenId}: {Error}", 
                    request.TokenId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Handles token blacklisting requests
        /// </summary>
        public async Task Consume(ConsumeContext<BlacklistTokenRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing token blacklist request for token: {TokenId}, Reason: {Reason}", 
                request.TokenId, request.Reason);

            try
            {
                var blacklistResult = await BlacklistTokenAsync(request);
                
                if (blacklistResult.Success)
                {
                    var response = blacklistResult.Value;
                    await context.RespondAsync(response);
                    _logger.LogInformation("Token successfully blacklisted: {TokenId}", request.TokenId);
                }
                else
                {
                    _logger.LogError("Failed to blacklist token {TokenId}: {Errors}", 
                        request.TokenId, string.Join(", ", blacklistResult.Errors.Select(e => e.Text)));
                    throw new InvalidOperationException($"Failed to blacklist token: {string.Join(", ", blacklistResult.Errors.Select(e => e.Text))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to blacklist token {TokenId}: {Error}", 
                    request.TokenId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Checks if a token is blacklisted using the database function
        /// </summary>
        private async Task<Result<CheckTokenBlacklistResponse>> CheckTokenBlacklistAsync(CheckTokenBlacklistRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<CheckTokenBlacklistResponse>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                const string query = @"
                    SELECT authz.is_token_blacklisted(@TokenId) as is_blacklisted";

                var isBlacklisted = await connection.QuerySingleAsync<bool>(query, new { TokenId = request.TokenId });

                if (!isBlacklisted)
                {
                    // Token is not blacklisted
                    var response = CheckTokenBlacklistResponse.NotBlacklisted(
                        tokenId: request.TokenId,
                        requestId: request.RequestId,
                        userId: request.UserId,
                        organizationId: request.OrganizationId);

                    return Result<CheckTokenBlacklistResponse>.Ok(response);
                }

                // Token is blacklisted - get details
                var blacklistDetails = await GetBlacklistDetailsAsync(connection, request.TokenId);

                var blacklistedResponse = CheckTokenBlacklistResponse.Blacklisted(
                    tokenId: request.TokenId,
                    blacklistedAt: blacklistDetails.BlacklistedAt,
                    blacklistReason: blacklistDetails.Reason,
                    blacklistedBy: blacklistDetails.BlacklistedBy,
                    requestId: request.RequestId,
                    userId: request.UserId,
                    organizationId: request.OrganizationId);

                return Result<CheckTokenBlacklistResponse>.Ok(blacklistedResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error checking token blacklist for {TokenId}: {Error}", 
                    request.TokenId, ex.Message);
                return Result<CheckTokenBlacklistResponse>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Blacklists a token using the database function
        /// </summary>
        private async Task<Result<BlacklistTokenResponse>> BlacklistTokenAsync(BlacklistTokenRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<BlacklistTokenResponse>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                const string query = @"
                    SELECT authz.blacklist_token(@TokenId, @Reason, @BlacklistedBy) as success";

                var success = await connection.QuerySingleAsync<bool>(query, new 
                { 
                    TokenId = request.TokenId,
                    Reason = request.Reason,
                    BlacklistedBy = request.RequestedBy
                });

                if (!success)
                {
                    return Result<BlacklistTokenResponse>.Fail(
                        RbacErrors.Authorization.TokenBlacklistFailed);
                }

                var response = BlacklistTokenResponse.CreateSuccess(
                    tokenId: request.TokenId,
                    reason: request.Reason,
                    userId: request.UserId,
                    organizationId: request.OrganizationId,
                    blacklistedBy: request.RequestedBy,
                    requestId: request.RequestId);

                return Result<BlacklistTokenResponse>.Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error blacklisting token {TokenId}: {Error}", 
                    request.TokenId, ex.Message);
                return Result<BlacklistTokenResponse>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Gets blacklist details for a token
        /// </summary>
        private async Task<BlacklistDetails> GetBlacklistDetailsAsync(IDbConnection connection, string tokenId)
        {
            const string query = @"
                SELECT 
                    blacklisted_at,
                    reason,
                    blacklisted_by
                FROM authz.token_blacklist 
                WHERE jti = @TokenId";

            var details = await connection.QuerySingleOrDefaultAsync<BlacklistDetailsData>(query, new { TokenId = tokenId });
            
            return new BlacklistDetails
            {
                BlacklistedAt = details?.BlacklistedAt ?? DateTime.UtcNow,
                Reason = details?.Reason ?? "Unknown",
                BlacklistedBy = details?.BlacklistedBy ?? "System"
            };
        }

        /// <summary>
        /// Blacklist details model
        /// </summary>
        private record BlacklistDetails
        {
            public DateTime BlacklistedAt { get; init; }
            public string Reason { get; init; } = string.Empty;
            public string? BlacklistedBy { get; init; }
        }

        /// <summary>
        /// Database model for blacklist details
        /// </summary>
        private record BlacklistDetailsData
        {
            public DateTime BlacklistedAt { get; init; }
            public string Reason { get; init; } = string.Empty;
            public string? BlacklistedBy { get; init; }
        }
    }
}