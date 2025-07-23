namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Example usage patterns for the Result<T> type in RBAC operations
    /// </summary>
    public static class ResultUsageExamples
    {
        // Example 1: Simple successful operation
        public static Result<string> GetUserName(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return RbacErrors.RbacValidation.InvalidExternalUserId(userId);

            return "John Doe"; // Implicit conversion from string to Result<string>
        }

        // Example 2: Operation that doesn't return a value
        public static Result UpdateUserProfile(string userId, string newName)
        {
            if (string.IsNullOrEmpty(userId))
                return RbacErrors.RbacValidation.InvalidExternalUserId(userId);

            // Simulate update operation
            return Result.Ok(); // Successful operation with no return value
        }

        // Example 3: Chaining operations with Then()
        public static async Task<Result<string>> GetUserDisplayNameAsync(string userId)
        {
            var userResult = await GetUserByIdAsync(userId);
            if (!userResult.Success)
                return Result<string>.Fail(userResult.Errors);

            var orgResult = await GetUserOrganizationAsync(userResult.Value.Id);
            if (!orgResult.Success)
                return Result<string>.Fail(orgResult.Errors);

            return await FormatDisplayNameAsync(userResult.Value, orgResult.Value);
        }

        // Example 4: Collecting multiple validation errors
        public static Result<UserProfile> ValidateUserProfile(UserProfile profile)
        {
            var errors = new List<ErrorMessage>();

            if (string.IsNullOrEmpty(profile.Email))
                errors.Add(RbacErrors.RbacValidation.InvalidExternalUserId("email"));

            if (string.IsNullOrEmpty(profile.FirstName))
                errors.Add(ErrorMessage.RbacValidation(4206, "First name is required", "Please enter your first name"));

            if (string.IsNullOrEmpty(profile.LastName))
                errors.Add(ErrorMessage.RbacValidation(4207, "Last name is required", "Please enter your last name"));

            if (errors.Any())
                return Result<UserProfile>.Fail(errors);

            return profile; // Implicit conversion to successful Result
        }

        // Example 5: Combining multiple Results
        public static async Task<Result<(UserProfile User, string[] Permissions)>> GetUserWithPermissionsAsync(string userId)
        {
            var userTask = GetUserByIdAsync(userId);
            var permissionsTask = GetUserPermissionsAsync(userId);

            var userResult = await userTask;
            var permissionsResult = await permissionsTask;

            // Combine results - will collect all errors if either fails
            var combinedResults = new[] { userResult.Select(u => (object)u), permissionsResult.Select(p => (object)p) }
                .Combine();

            if (!combinedResults.Success)
                return Result<(UserProfile, string[])>.Fail(combinedResults.Errors);

            var results = combinedResults.Value.ToArray();
            return ((UserProfile)results[0], (string[])results[1]);
        }

        // Example helper methods (would be implemented elsewhere)
        private static async Task<Result<UserProfile>> GetUserByIdAsync(string userId)
        {
            await Task.Delay(10); // Simulate async operation
            
            if (userId == "invalid")
                return RbacErrors.RbacValidation.UserNotFound;

            return new UserProfile { Id = userId, Email = "user@example.com", FirstName = "John", LastName = "Doe" };
        }

        private static async Task<Result<string>> GetUserOrganizationAsync(string userId)
        {
            await Task.Delay(10); // Simulate async operation
            return "Engineering Department";
        }

        private static async Task<Result<string[]>> GetUserPermissionsAsync(string userId)
        {
            await Task.Delay(10); // Simulate async operation
            return new[] { "users:read", "users:write", "projects:read" };
        }

        private static async Task<Result<string>> FormatDisplayNameAsync(UserProfile user, string organization)
        {
            await Task.Delay(10); // Simulate async operation
            return $"{user.FirstName} {user.LastName} ({organization})";
        }
    }
}