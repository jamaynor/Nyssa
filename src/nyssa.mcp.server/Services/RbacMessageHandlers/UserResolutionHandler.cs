using MassTransit;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Models;
using Nyssa.Mcp.Server.Models.Messages.Authentication;
using Dapper;
using System.Data;

namespace Nyssa.Mcp.Server.Services.RbacMessageHandlers
{
    /// <summary>
    /// Message handler for user resolution operations
    /// </summary>
    public class UserResolutionHandler : 
        IConsumer<ResolveUserRequest>,
        IConsumer<CreateUserRequest>
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private readonly ILogger<UserResolutionHandler> _logger;

        public UserResolutionHandler(
            IDatabaseConnectionFactory connectionFactory,
            ILogger<UserResolutionHandler> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        /// <summary>
        /// Handles user resolution by external WorkOS ID
        /// </summary>
        public async Task Consume(ConsumeContext<ResolveUserRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing user resolution request for WorkOS user: {WorkOSUserId}", request.WorkOSUserId);

            try
            {
                var userResult = await ResolveUserByExternalIdAsync(request.WorkOSUserId);
                
                if (userResult.Success)
                {
                    var user = userResult.Value;
                    var response = new ResolveUserResponse(
                        userId: user.Id,
                        externalUserId: user.ExternalUserId,
                        email: user.Email,
                        firstName: user.FirstName,
                        lastName: user.LastName,
                        createdAt: user.CreatedAt,
                        updatedAt: user.UpdatedAt,
                        isNewUser: false,
                        profilePictureUrl: user.ProfilePictureUrl,
                        requestId: request.RequestId);

                    await context.RespondAsync(response);
                    _logger.LogInformation("User resolved successfully: {UserId}", user.Id);
                }
                else
                {
                    // User not found - this is normal for new users
                    _logger.LogInformation("User not found for WorkOS ID: {WorkOSUserId}. Will need user creation.", request.WorkOSUserId);
                    
                    // Respond with error indicating user needs to be created
                    var errorResponse = new ResolveUserResponse
                    {
                        RequestId = request.RequestId,
                        ResponseAt = DateTime.UtcNow
                    };
                    
                    await context.RespondAsync(errorResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve user {WorkOSUserId}: {Error}", request.WorkOSUserId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Handles user creation requests
        /// </summary>
        public async Task Consume(ConsumeContext<CreateUserRequest> context)
        {
            var request = context.Message;
            _logger.LogInformation("Processing user creation request for WorkOS user: {ExternalUserId}", request.ExternalUserId);

            try
            {
                var createResult = await CreateUserAsync(request);
                
                if (createResult.Success)
                {
                    var user = createResult.Value;
                    var response = new CreateUserResponse(
                        userId: user.Id,
                        externalUserId: user.ExternalUserId,
                        email: user.Email,
                        firstName: user.FirstName,
                        lastName: user.LastName,
                        createdAt: user.CreatedAt,
                        profilePictureUrl: user.ProfilePictureUrl,
                        requestId: request.RequestId);

                    await context.RespondAsync(response);
                    _logger.LogInformation("User created successfully: {UserId}", user.Id);
                }
                else
                {
                    _logger.LogError("Failed to create user for WorkOS ID: {ExternalUserId}, Errors: {Errors}", 
                        request.ExternalUserId, string.Join(", ", createResult.Errors.Select(e => e.Text)));
                    throw new InvalidOperationException($"Failed to create user: {string.Join(", ", createResult.Errors.Select(e => e.Text))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user {ExternalUserId}: {Error}", request.ExternalUserId, ex.Message);
                throw; // Let MassTransit handle the retry/error logic
            }
        }

        /// <summary>
        /// Resolves a user by their external WorkOS ID
        /// </summary>
        private async Task<Result<RbacUser>> ResolveUserByExternalIdAsync(string workOSUserId)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<RbacUser>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                const string query = @"
                    SELECT 
                        id,
                        external_id,
                        email,
                        first_name,
                        last_name,
                        profile_picture_url,
                        created_at,
                        updated_at
                    FROM authz.users 
                    WHERE external_id = @ExternalId 
                    AND deleted_at IS NULL";

                var user = await connection.QuerySingleOrDefaultAsync<RbacUserData>(query, new { ExternalId = workOSUserId });
                
                if (user == null)
                {
                    return Result<RbacUser>.Fail(RbacErrors.Authentication.UserNotFound);
                }

                return Result<RbacUser>.Ok(new RbacUser(
                    id: user.Id,
                    externalUserId: user.ExternalId,
                    email: user.Email,
                    firstName: user.FirstName,
                    lastName: user.LastName,
                    profilePictureUrl: user.ProfilePictureUrl,
                    createdAt: user.CreatedAt,
                    updatedAt: user.UpdatedAt));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error resolving user {WorkOSUserId}: {Error}", workOSUserId, ex.Message);
                return Result<RbacUser>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Creates a new user in the RBAC system
        /// </summary>
        private async Task<Result<RbacUser>> CreateUserAsync(CreateUserRequest request)
        {
            var connectionResult = await _connectionFactory.CreateConnectionAsync();
            if (!connectionResult.Success)
                return Result<RbacUser>.Fail(connectionResult.Errors);

            try
            {
                using var connection = connectionResult.Value;
                
                const string query = @"
                    INSERT INTO authz.users 
                        (external_id, email, first_name, last_name, profile_picture_url, created_at, updated_at)
                    VALUES 
                        (@ExternalId, @Email, @FirstName, @LastName, @ProfilePictureUrl, @CreatedAt, @UpdatedAt)
                    RETURNING 
                        id,
                        external_id,
                        email,
                        first_name,
                        last_name,
                        profile_picture_url,
                        created_at,
                        updated_at";

                var now = DateTime.UtcNow;
                var user = await connection.QuerySingleAsync<RbacUserData>(query, new
                {
                    ExternalId = request.ExternalUserId,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    ProfilePictureUrl = request.ProfilePictureUrl,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                return Result<RbacUser>.Ok(new RbacUser(
                    id: user.Id,
                    externalUserId: user.ExternalId,
                    email: user.Email,
                    firstName: user.FirstName,
                    lastName: user.LastName,
                    profilePictureUrl: user.ProfilePictureUrl,
                    createdAt: user.CreatedAt,
                    updatedAt: user.UpdatedAt));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database error creating user {ExternalUserId}: {Error}", request.ExternalUserId, ex.Message);
                return Result<RbacUser>.Fail(RbacErrors.Database.QueryExecutionFailed);
            }
        }

        /// <summary>
        /// Database model for user data
        /// </summary>
        private record RbacUserData
        {
            public string Id { get; init; } = string.Empty;
            public string ExternalId { get; init; } = string.Empty;
            public string Email { get; init; } = string.Empty;
            public string FirstName { get; init; } = string.Empty;
            public string LastName { get; init; } = string.Empty;
            public string? ProfilePictureUrl { get; init; }
            public DateTime CreatedAt { get; init; }
            public DateTime UpdatedAt { get; init; }
        }
    }

    /// <summary>
    /// RBAC User model
    /// </summary>
    public record RbacUser
    {
        public string Id { get; init; }
        public string ExternalUserId { get; init; }
        public string Email { get; init; }
        public string FirstName { get; init; }
        public string LastName { get; init; }
        public string? ProfilePictureUrl { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }

        public RbacUser(
            string id,
            string externalUserId,
            string email,
            string firstName,
            string lastName,
            string? profilePictureUrl,
            DateTime createdAt,
            DateTime updatedAt)
        {
            Id = id;
            ExternalUserId = externalUserId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            ProfilePictureUrl = profilePictureUrl;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }
}