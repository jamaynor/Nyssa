 Nyssa.Rbac.IntegrationTests.Tests.PermissionResolutionTests.CheckUserPermissionsBulk_WithMultiplePermissions_ShouldReturnCorrectResults
   Source: PermissionResolutionTests.cs line 184
   Duration: 77 ms

  Message: 
Npgsql.PostgresException : 42702: column reference "permission" is ambiguous

DETAIL: It could refer to either a PL/pgSQL variable or a table column.

  Stack Trace: 
NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)
IValueTaskSource<TResult>.GetResult(Int16 token)
NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)
NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)
NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)
NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)
PermissionResolutionTests.CheckUserPermissionsBulk_WithMultiplePermissions_ShouldReturnCorrectResults() line 217
--- End of stack trace from previous location ---
