---
description: Result pattern for error handling and operation chaining
globs: "**/*.cs"
---

# Result Pattern Rules

You are an expert in functional error handling using the Result pattern for robust, explicit error management.

## Result Type Structure
- Use Result<T, ErrorMessage> for all methods that can reasonably fail
- Result contains a Value property for success data and an Errors collection
- Success property is computed: true if zero errors, false if one or more errors
- Never throw exceptions for expected business failures - use Result instead

## ErrorMessage Structure
- ErrorMessage contains three properties: Code, Text, and UserFriendlyText
- Code: HTTP-style error code for categorization and processing
- Text: Technical error description for developers and logging
- UserFriendlyText: User-facing error message for UI display

## HTTP-Style Error Codes
- Use HTTP status code semantics: 4xxx for client errors, 5xxx for server errors
- Group codes by subdomain within each category for organization
- Examples: 4001-4099 Authentication, 4100-4199 Patient validation, 5001-5099 Database errors
- Maintain consistency across all assemblies and bounded contexts

## Error Collection and Validation
- Collect multiple validation errors in a single Result rather than failing on first error
- Return all relevant validation failures to provide complete feedback
- Use specific error codes for each type of validation failure
- Aggregate related validation errors under appropriate subdomains

## Result Consumption Patterns
- Use properties approach: check result.Success, then access result.Value or result.Errors
- Avoid Match() methods in favor of simple conditional logic
- Handle errors immediately after receiving Result to maintain clear control flow
- Never access Value property without first checking Success property

## Result Chaining with Then()
- Implement Then() method for chaining operations that return Results
- Then() only executes if the current Result is successful
- Errors shortcut the chain - subsequent operations are skipped
- Chain preserves and propagates errors through the entire operation sequence
- Use Then() to create readable, composable operation pipelines

## Business Operation Integration
- All domain methods that can fail should return Result<TPayloadType>
- Repository operations should return Results for database failures
- Validation operations should collect and return multiple errors
- API endpoints should convert Results to appropriate HTTP responses

## Error Code Categories
- 401-409: Authentication and authorization failures
- 410-419: Input validation errors
- 420-429: Business rule violations
- 430-439: Resource not found errors
- 501-509: Database and persistence errors
- 510-519: External service failures
- 520-529: Infrastructure and system errors

## Performance and Memory Considerations
- Use records for Result types when possible
- Cache common error messages to reduce allocations
- Avoid creating Result objects in hot paths unless necessary
- Consider using Result<Unit> for operations that don't return data

## Async Operation Patterns
- Use Result<TPayloadType> with async/await patterns consistently
- Chain async operations using Then() with proper Task handling
- Ensure error propagation works correctly across async boundaries
- Handle cancellation tokens appropriately in Result-returning methods

## Testing and Debugging
- Create helper methods for common Result creation patterns
- Test both success and failure paths for all Result-returning methods
- Use descriptive error codes and messages for easier debugging
- Log error codes and technical messages for operational monitoring

## Integration with Domain Events
- Raise domain events only for successful operations
- Include error information in failed operation logging
- Use Result pattern consistently in event handlers and domain services
- Ensure event processing failures are handled through Result pattern

## Conversion and Interoperability
- Provide implicit conversions from TPayloadType to Result<TPayloadType> for success cases
- Create factory methods for common error scenarios
- Implement extension methods for common Result operations
- Ensure Result pattern works well with dependency injection and services