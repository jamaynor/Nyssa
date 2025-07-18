# RBAC Database Implementation

This directory contains the PostgreSQL database implementation for the Multi-Organization RBAC (Role-Based Access Control) system.

## Overview

The RBAC system provides enterprise-grade role-based access control for multi-tenant applications with support for:
- Hierarchical organizations using PostgreSQL LTREE
- Custom roles per organization with permission inheritance
- JWT token management with blacklisting
- Comprehensive audit logging
- Sub-100ms permission resolution performance

## Directory Structure

```
database/authorization/
├── schema/                 # Schema creation and extensions
│   └── 01_create_schema.sql
├── tables/                 # Table definitions (one file per table)
│   ├── 01_organizations.sql
│   ├── 02_users.sql
│   ├── 03_organization_memberships.sql
│   ├── 04_roles.sql
│   ├── 05_permissions.sql
│   ├── 06_role_permissions.sql
│   ├── 07_user_roles.sql
│   ├── 08_token_blacklist.sql
│   └── 09_audit_events.sql
├── functions/              # Database functions
│   ├── 01_organization_management.sql
│   ├── 02_permission_resolution.sql
│   ├── 03_role_management.sql
│   └── 04_token_audit_management.sql
├── views/                  # Materialized views
│   └── 01_user_effective_permissions.sql
├── tests/                  # Comprehensive test suite
│   ├── 01_test_organization_management.sql
│   ├── 02_test_permission_resolution.sql
│   ├── 03_test_role_management.sql
│   └── 04_test_token_audit.sql
├── migrations/             # Migration scripts
│   └── 01_initial_setup.sql
└── README.md              # This file
```

## Installation

### Prerequisites

- PostgreSQL 16 or higher
- Required extensions (installed automatically):
  - `uuid-ossp` - UUID generation
  - `ltree` - Hierarchical organization paths
  - `pgcrypto` - Encryption capabilities
  - `pg_trgm` - Text search optimization
  - `btree_gist` - Advanced indexing

### Quick Setup

1. Connect to your PostgreSQL database:
```bash
psql -U postgres -d your_database
```

2. Run the master migration script:
```sql
\i database/authorization/migrations/01_initial_setup.sql
```

This will:
- Create the `authorization` schema
- Install required extensions
- Create all tables with proper indexes
- Install all database functions
- Create materialized views
- Insert initial permission data
- Set up scheduled jobs (if pg_cron is available)
- Validate the installation

### Manual Setup

If you prefer to set up components individually:

```sql
-- 1. Create schema and extensions
\i database/authorization/schema/01_create_schema.sql

-- 2. Create tables (order matters due to foreign keys)
\i database/authorization/tables/02_users.sql
\i database/authorization/tables/01_organizations.sql
\i database/authorization/tables/03_organization_memberships.sql
-- ... continue with remaining tables

-- 3. Create functions
\i database/authorization/functions/04_token_audit_management.sql
\i database/authorization/functions/01_organization_management.sql
-- ... continue with remaining functions

-- 4. Create views
\i database/authorization/views/01_user_effective_permissions.sql
```

## Core Concepts

### Organizations

Organizations form a hierarchy using PostgreSQL's LTREE extension:
- Root organizations have simple paths (e.g., `acme`)
- Child organizations have dot-separated paths (e.g., `acme.engineering.backend`)
- Efficient ancestor/descendant queries using LTREE operators

### Permissions

Permissions follow a `resource:action` format:
- **Table permissions**: `users:read`, `users:write`, `users:delete`
- **System permissions**: `system:admin`, `system:manage_roles`
- **Feature permissions**: `feature:advanced_analytics`
- **API permissions**: `api:external_integration`

### Permission Inheritance

- Roles marked as `is_inheritable = true` cascade down the organization hierarchy
- Direct permissions always override inherited permissions
- Role priority determines precedence when conflicts occur

### Token Management

- JWT tokens are validated against the blacklist for immediate revocation
- Emergency revocation blocks all tokens for a user
- Automatic cleanup of expired tokens

## Key Functions

### Organization Management

```sql
-- Create an organization
SELECT * FROM authorization.create_organization(
    'Engineering',              -- name
    'Engineering Department',   -- display_name
    'Main engineering team',    -- description
    parent_org_id,             -- parent_id (NULL for root)
    created_by_user_id,        -- created_by
    '{"team_size": 50}'        -- metadata
);

-- Move organization to new parent
SELECT authorization.move_organization(
    org_id,                    -- organization to move
    new_parent_id,             -- new parent (NULL for root)
    moved_by_user_id           -- user making the change
);

-- Get organization hierarchy
SELECT * FROM authorization.get_organization_hierarchy(
    user_id,                   -- filter by user access (NULL for all)
    root_org_id,               -- start from specific org (NULL for all)
    max_depth,                 -- limit depth (NULL for unlimited)
    include_inactive           -- include inactive orgs
);
```

### Permission Checking

```sql
-- Check single permission
SELECT * FROM authorization.check_user_permission(
    user_id,
    organization_id,
    'users:write',              -- permission to check
    true                        -- include inherited permissions
);

-- Check multiple permissions efficiently
SELECT * FROM authorization.check_user_permissions_bulk(
    user_id,
    organization_id,
    ARRAY['users:read', 'users:write', 'roles:manage'],
    true                        -- include inherited
);

-- Get all user permissions
SELECT * FROM authorization.resolve_user_permissions(
    user_id,
    organization_id,
    true,                       -- include inherited
    'users:%'                   -- filter pattern (NULL for all)
);
```

### Role Management

```sql
-- Assign role to user
SELECT * FROM authorization.assign_user_role(
    user_id,
    role_id,
    organization_id,
    granted_by_user_id,
    '2024-12-31'::timestamp,    -- expires_at (NULL for permanent)
    '{"ip_required": "10.0.0.0/8"}',  -- conditions
    '{}'                        -- metadata
);

-- Revoke role from user
SELECT authorization.revoke_user_role(
    user_id,
    role_id,
    organization_id,
    revoked_by_user_id,
    'Position change'           -- reason
);

-- Add permission to role
SELECT authorization.add_permission_to_role(
    role_id,
    'users:write',              -- permission string
    granted_by_user_id,
    '{}',                       -- conditions
    '{}'                        -- metadata
);
```

### Token Management

```sql
-- Blacklist a token
SELECT authorization.blacklist_token(
    'jwt_token_jti',           -- JWT ID claim
    user_id,
    organization_id,
    revoked_by_user_id,
    'Logout',                  -- reason
    token_expires_at           -- token expiration
);

-- Emergency revoke all user tokens
SELECT authorization.emergency_revoke_user_tokens(
    user_id,
    revoked_by_user_id,
    'Security incident'        -- reason
);

-- Check if token is blacklisted
SELECT authorization.is_token_blacklisted('jwt_token_jti');
```

## Testing

The test suite provides comprehensive coverage of all functions:

```sql
-- Run all tests
\i database/authorization/tests/01_test_organization_management.sql
\i database/authorization/tests/02_test_permission_resolution.sql
\i database/authorization/tests/03_test_role_management.sql
\i database/authorization/tests/04_test_token_audit.sql
```

Each test suite:
- Creates isolated test data with `test_context` metadata
- Tests both success and failure scenarios
- Validates data integrity and business rules
- Cleans up after execution
- Reports detailed results with RAISE NOTICE

## Performance Optimization

### Indexes

All tables have comprehensive indexes for optimal query performance:
- LTREE GIST indexes for hierarchy queries
- B-tree indexes for lookups and joins
- GIN indexes for JSONB metadata queries
- Partial indexes for common WHERE clauses

### Materialized Views

The `user_effective_permissions` materialized view pre-computes direct permissions for fast lookups:
- Refresh every 5 minutes (configurable)
- Concurrent refresh to avoid blocking
- Used as fallback when JWT permissions are stale

### Query Optimization

- Permission checks designed for <100ms p95 latency
- Bulk operations to reduce round trips
- Efficient LTREE operators for hierarchy traversal
- Connection pooling recommended (PgBouncer)

## Maintenance

### Scheduled Jobs

If pg_cron is installed, the following jobs are created automatically:
- **Token cleanup**: Hourly removal of expired tokens
- **Role expiration**: Every 15 minutes to deactivate expired roles
- **View refresh**: Every 5 minutes for permission cache
- **Partition creation**: Monthly for audit events

### Manual Maintenance

```sql
-- Cleanup expired tokens
SELECT authorization.cleanup_expired_tokens();

-- Expire user roles
SELECT authorization.expire_user_roles();

-- Refresh permission cache
REFRESH MATERIALIZED VIEW CONCURRENTLY authorization.user_effective_permissions;

-- Create next month's audit partition
SELECT authorization.create_monthly_partition();
```

### Monitoring

Monitor these key metrics:
- Permission check latency (target: <100ms p95)
- Materialized view refresh time
- Audit event partition sizes
- Token blacklist size
- Active user sessions

## Security Considerations

1. **Audit Everything**: All permission checks and administrative actions are logged
2. **Immutable Audit Trail**: Audit events cannot be updated or deleted
3. **Token Revocation**: Immediate effect through blacklist checking
4. **Emergency Access**: Special functions for security incidents
5. **Encrypted Storage**: Sensitive data can use pgcrypto functions

## Troubleshooting

### Common Issues

1. **LTREE extension not found**
   ```sql
   CREATE EXTENSION ltree;
   ```

2. **Permission denied errors**
   - Check user has organization membership
   - Verify role is active and not expired
   - Confirm organization is active

3. **Slow permission checks**
   - Refresh materialized view
   - Check index usage with EXPLAIN
   - Consider increasing cache duration

4. **Circular organization reference**
   - The move_organization function prevents this
   - Check organization paths for consistency

### Debug Queries

```sql
-- Check user's direct roles
SELECT * FROM authorization.get_user_roles(user_id, organization_id, false);

-- View organization hierarchy
SELECT id, name, path, parent_id, level 
FROM authorization.get_organization_hierarchy()
ORDER BY path;

-- Recent audit events for user
SELECT * FROM authorization.get_audit_events(
    start_date => CURRENT_TIMESTAMP - INTERVAL '1 hour',
    p_user_id => user_id,
    p_limit => 100
);

-- Permission resolution details
SELECT * FROM authorization.resolve_user_permissions(user_id, org_id, true, NULL)
ORDER BY source, permission;
```

## Support

For issues or questions:
1. Check the test files for usage examples
2. Review function comments in the SQL files
3. Consult the technical documentation in `/docs/rbac-technical.md`