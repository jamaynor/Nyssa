-- ============================================================================
-- File: 01_initial_setup.sql
-- Description: Master migration script for RBAC system initial setup
-- Author: Platform Team
-- Created: January 2024
-- Version: 1.0.0
-- ============================================================================

-- This script sets up the complete RBAC database schema in the correct order

BEGIN;

-- ============================================================================
-- 1. Schema and Extensions
-- ============================================================================
\echo 'Creating schema and extensions...'
\i ../schema/01_create_schema.sql

-- ============================================================================
-- 2. Core Tables (order matters due to foreign keys)
-- ============================================================================
\echo 'Creating core tables...'

-- Users table (no dependencies)
\i ../tables/02_users.sql

-- Organizations table (no dependencies)
\i ../tables/01_organizations.sql

-- Organization memberships (depends on users and organizations)
\i ../tables/03_organization_memberships.sql

-- Roles table (depends on organizations)
\i ../tables/04_roles.sql

-- Permissions table (no dependencies)
\i ../tables/05_permissions.sql

-- Role permissions (depends on roles and permissions)
\i ../tables/06_role_permissions.sql

-- User roles (depends on users, roles, and organizations)
\i ../tables/07_user_roles.sql

-- Token blacklist (depends on users and organizations)
\i ../tables/08_token_blacklist.sql

-- Audit events (depends on users and organizations)
\i ../tables/09_audit_events.sql

-- ============================================================================
-- 3. Core Functions
-- ============================================================================
\echo 'Creating core functions...'

-- Audit functions first (used by other functions)
\i ../functions/04_token_audit_management.sql

-- Organization management functions
\i ../functions/01_organization_management.sql

-- Permission resolution functions
\i ../functions/02_permission_resolution.sql

-- Role management functions
\i ../functions/03_role_management.sql

-- ============================================================================
-- 4. Materialized Views
-- ============================================================================
\echo 'Creating materialized views...'
\i ../views/01_user_effective_permissions.sql

-- ============================================================================
-- 5. Initial Data Setup
-- ============================================================================
\echo 'Setting up initial data...'

-- Create system permissions
INSERT INTO authz.permissions (permission, display_name, category, is_system_permission) VALUES
	-- System permissions
	('system:admin', 'System Administrator', 'system', true),
	('system:manage_organizations', 'Manage Organizations', 'system', true),
	('system:manage_users', 'Manage Users', 'system', true),
	('system:manage_roles', 'Manage Roles', 'system', true),
	('system:view_audit', 'View Audit Logs', 'system', true),
	('system:emergency_access', 'Emergency Access Control', 'system', true),
	
	-- Table permissions
	('users:read', 'Read Users', 'table', false),
	('users:write', 'Write Users', 'table', false),
	('users:delete', 'Delete Users', 'table', false),
	
	('organizations:read', 'Read Organizations', 'table', false),
	('organizations:write', 'Write Organizations', 'table', false),
	('organizations:delete', 'Delete Organizations', 'table', false),
	
	('roles:read', 'Read Roles', 'table', false),
	('roles:write', 'Write Roles', 'table', false),
	('roles:delete', 'Delete Roles', 'table', false),
	
	-- Feature permissions
	('feature:advanced_analytics', 'Advanced Analytics', 'feature', false),
	('feature:export_data', 'Export Data', 'feature', false),
	('feature:bulk_operations', 'Bulk Operations', 'feature', false),
	
	-- API permissions
	('api:external_integration', 'External API Integration', 'api', false),
	('api:reporting', 'Reporting API', 'api', false)
ON CONFLICT (permission) DO NOTHING;

-- ============================================================================
-- 6. Refresh Materialized View
-- ============================================================================
\echo 'Refreshing materialized views...'
REFRESH MATERIALIZED VIEW authz.user_effective_permissions;

-- ============================================================================
-- 7. Create scheduled jobs (if pg_cron is available)
-- ============================================================================
\echo 'Setting up scheduled jobs...'

-- Check if pg_cron extension is available
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_cron') THEN
		-- Schedule token cleanup every hour
		PERFORM cron.schedule(
			'cleanup-expired-tokens',
			'0 * * * *',  -- Every hour
			'SELECT authorization.cleanup_expired_tokens();'
		);
		
		-- Schedule role expiration check every 15 minutes
		PERFORM cron.schedule(
			'expire-user-roles',
			'*/15 * * * *',  -- Every 15 minutes
			'SELECT authorization.expire_user_roles();'
		);
		
		-- Schedule materialized view refresh every 5 minutes
		PERFORM cron.schedule(
			'refresh-user-permissions',
			'*/5 * * * *',  -- Every 5 minutes
			'REFRESH MATERIALIZED VIEW CONCURRENTLY authorization.user_effective_permissions;'
		);
		
		-- Schedule monthly partition creation
		PERFORM cron.schedule(
			'create-audit-partition',
			'0 0 25 * *',  -- 25th of each month at midnight
			'SELECT authorization.create_monthly_partition();'
		);
		
		RAISE NOTICE 'Scheduled jobs created successfully';
	ELSE
		RAISE NOTICE 'pg_cron extension not found - scheduled jobs not created';
		RAISE NOTICE 'To enable scheduled jobs, install pg_cron and re-run the scheduling section';
	END IF;
EXCEPTION
	WHEN OTHERS THEN
		RAISE NOTICE 'Error setting up scheduled jobs: %', SQLERRM;
		RAISE NOTICE 'You may need to set up scheduled jobs manually';
END;
$$;

-- ============================================================================
-- 8. Grant Permissions
-- ============================================================================
\echo 'Setting up permissions...'

-- Create application role if it doesn't exist
DO $$
BEGIN
	IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rbac_app_user') THEN
		CREATE ROLE rbac_app_user;
	END IF;
END;
$$;

-- Grant schema usage
GRANT USAGE ON SCHEMA authz TO rbac_app_user;

-- Grant table permissions
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA authz TO rbac_app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA authz TO rbac_app_user;

-- Grant function execution
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA authz TO rbac_app_user;

-- Audit events should be insert-only for the application
REVOKE UPDATE, DELETE ON authz.audit_events FROM rbac_app_user;

-- ============================================================================
-- 9. Create indexes for partition tables
-- ============================================================================
\echo 'Creating partition indexes...'

-- Create indexes on initial partitions
CREATE INDEX IF NOT EXISTS idx_audit_y2024m01_occurred ON authz.audit_events_y2024m01(occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_y2024m02_occurred ON authz.audit_events_y2024m02(occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_y2024m03_occurred ON authz.audit_events_y2024m03(occurred_at DESC);

-- ============================================================================
-- 10. Validation
-- ============================================================================
\echo 'Validating installation...'

DO $$
DECLARE
	table_count INTEGER;
	function_count INTEGER;
	permission_count INTEGER;
BEGIN
	-- Check tables
	SELECT COUNT(*) INTO table_count
	FROM information_schema.tables
	WHERE table_schema = 'authz'
		AND table_type = 'BASE TABLE';
	
	IF table_count < 9 THEN
		RAISE EXCEPTION 'Expected at least 9 tables, found %', table_count;
	END IF;
	
	-- Check functions
	SELECT COUNT(*) INTO function_count
	FROM information_schema.routines
	WHERE routine_schema = 'authz'
		AND routine_type = 'FUNCTION';
	
	IF function_count < 20 THEN
		RAISE EXCEPTION 'Expected at least 20 functions, found %', function_count;
	END IF;
	
	-- Check permissions
	SELECT COUNT(*) INTO permission_count
	FROM authz.permissions;
	
	IF permission_count < 15 THEN
		RAISE EXCEPTION 'Expected at least 15 permissions, found %', permission_count;
	END IF;
	
	RAISE NOTICE 'Validation passed: % tables, % functions, % permissions', 
		table_count, function_count, permission_count;
END;
$$;

COMMIT;

\echo ''
\echo '============================================================================'
\echo 'RBAC system installation completed successfully!'
\echo '============================================================================'
\echo ''
\echo 'Next steps:'
\echo '1. Create your first organization using: SELECT * FROM authorization.create_organization(...)'
\echo '2. Create users and assign them to organizations'
\echo '3. Create roles and assign permissions'
\echo '4. Run tests using: \i ../tests/*.sql'
\echo ''
\echo 'For documentation, see: /docs/rbac-technical.md'
\echo '============================================================================'