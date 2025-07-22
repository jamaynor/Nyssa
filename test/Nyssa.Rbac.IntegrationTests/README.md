# Nyssa RBAC Integration Tests

This project contains comprehensive integration tests for the Nyssa RBAC (Role-Based Access Control) system.

## Overview

The integration tests validate the complete RBAC system functionality including:

- **Admin Organization Hierarchy**: Tests that all organizations descend from the Admin parent
- **Organization Management**: Creation, modification, and hierarchy operations
- **Permission Resolution**: Direct and inherited permission checking
- **Role Management**: Role assignment and permission linking
- **Token & Audit Management**: Security and audit trail functionality

## Test Structure

```
Tests/
├── AdminHierarchyTests.cs          # Admin parent organization enforcement
├── OrganizationManagementTests.cs  # Organization CRUD and hierarchy
├── PermissionResolutionTests.cs    # Permission checking and inheritance
├── RoleManagementTests.cs          # Role assignment and management
├── TokenAuditTests.cs              # Security tokens and audit logging
└── EndToEndScenarioTests.cs        # Complete workflow scenarios
```

## Key Features

### 🧪 **Test Framework Stack**
- **XUnit**: Primary testing framework
- **FluentAssertions**: Readable assertions
- **TestContainers**: Isolated PostgreSQL instances
- **Npgsql**: PostgreSQL data access

### 🐳 **Database Testing**
- **Isolated Environments**: Each test run gets a fresh PostgreSQL container
- **Schema Initialization**: Complete RBAC schema setup automatically
- **Test Data Management**: Automatic cleanup between tests
- **Real Database**: Tests against actual PostgreSQL, not mocks

### 🏗️ **Test Infrastructure**
- **DatabaseFixture**: Manages PostgreSQL TestContainer lifecycle
- **RbacTestBase**: Base class with helper methods for common operations
- **Test Data Builders**: Fluent builders for creating test data
- **Comprehensive Cleanup**: Ensures tests don't interfere with each other

## Running the Tests

### Prerequisites
- .NET 8.0 SDK
- Docker (for TestContainers)

### Command Line
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=AdminHierarchyTests"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Visual Studio
1. Open the solution in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. Open Test Explorer (Test → Test Explorer)
4. Click "Run All Tests"

## Test Categories

### Admin Hierarchy Tests
```csharp
[Fact]
public async Task AdminOrganization_ShouldExist_WithCorrectProperties()
[Fact] 
public async Task CreateOrganization_WithoutParent_ShouldAutomaticallyAssignAdminAsParent()
[Fact]
public async Task AllOrganizations_ExceptAdmin_ShouldDescendFromAdmin()
```

### Organization Management Tests
```csharp
[Fact]
public async Task CreateOrganization_WithValidData_ShouldSucceed()
[Fact]
public async Task MoveOrganization_ToNewParent_ShouldUpdatePathAndDescendants()
[Fact]
public async Task UserHasOrganizationAccess_WithMembership_ShouldReturnTrue()
```

### Permission Resolution Tests
```csharp
[Fact]
public async Task ResolveUserPermissions_WithDirectRole_ShouldReturnPermissions()
[Fact]
public async Task ResolveUserPermissions_WithInheritableRole_ShouldInheritFromParent()
[Fact]
public async Task CheckUserPermissionsBulk_WithMultiplePermissions_ShouldReturnCorrectResults()
```

## Configuration

The tests use `appsettings.Test.json` for configuration:

```json
{
  "Testing": {
    "DatabaseProvider": "PostgreSQL",
    "UseTestContainers": true,
    "CleanupAfterTests": true
  },
  "Rbac": {
    "AdminOrganizationId": "00000000-0000-0000-0000-000000000001",
    "DefaultTimeout": "00:02:00"
  }
}
```

## Test Data Management

### Automatic Cleanup
```csharp
public async Task CleanupTestDataAsync()
{
    // Removes all test data between tests using metadata flags
    DELETE FROM authz.* WHERE metadata->>'test_context' = 'true'
}
```

### Test Data Builders
```csharp
// Helper methods in RbacTestBase
var userId = await CreateTestUserAsync("test@example.com");
var (orgId, name, path) = await CreateTestOrganizationAsync("TestCorp");  
var roleId = await CreateTestRoleAsync(orgId, "TestRole");
```

## Validation Coverage

The tests validate all key RBAC requirements:

- ✅ **Admin Organization**: Fixed UUID `00000000-0000-0000-0000-000000000001`
- ✅ **Hierarchy Enforcement**: All orgs descend from Admin
- ✅ **Permission Inheritance**: Roles inherit through organization tree
- ✅ **Access Control**: Users can only access organizations they belong to
- ✅ **Audit Trail**: All operations generate audit events
- ✅ **Data Integrity**: Foreign key constraints and validation rules

## Troubleshooting

### Docker Issues
```bash
# Verify Docker is running
docker version

# Clean up test containers
docker container prune -f
```

### Test Failures
1. Check Docker is running and accessible
2. Ensure no port conflicts (PostgreSQL default 5432)
3. Verify all migrations run successfully during setup
4. Check test isolation - each test should clean up properly

## Contributing

When adding new tests:

1. **Inherit from RbacTestBase** for database access and helpers
2. **Use test metadata** `{"test_context": true}` for cleanup
3. **Follow AAA pattern** (Arrange, Act, Assert)
4. **Use FluentAssertions** for readable test assertions
5. **Test both success and failure cases**

## Performance Notes

- **Parallel Execution**: Tests run in parallel where possible
- **Container Reuse**: Database fixture is shared across test classes
- **Cleanup Efficiency**: Bulk delete operations for test data
- **Schema Caching**: Database schema is initialized once per fixture

The integration tests provide comprehensive validation of the RBAC system to ensure it meets all functional and security requirements.