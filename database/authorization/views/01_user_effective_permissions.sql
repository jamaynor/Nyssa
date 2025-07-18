-- ============================================================================
-- File: 01_user_effective_permissions.sql
-- Description: Materialized view for caching user effective permissions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Drop existing view if needed (for clean installs)
-- DROP MATERIALIZED VIEW IF EXISTS authorization.user_effective_permissions CASCADE;

-- ============================================================================
-- Materialized View: user_effective_permissions
-- Description: Pre-computed view of all user permissions for performance
-- ============================================================================
CREATE MATERIALIZED VIEW authorization.user_effective_permissions AS
SELECT 
	ur.user_id,
	ur.organization_id,
	o.path as org_path,
	o.name as org_name,
	p.permission,
	p.category as permission_category,
	p.resource,
	p.action,
	r.name as role_name,
	r.id as role_id,
	r.is_inheritable,
	r.priority,
	'direct'::VARCHAR as permission_source,
	ur.granted_at,
	ur.expires_at,
	ur.conditions
FROM authorization.user_roles ur
JOIN authorization.roles r ON ur.role_id = r.id
JOIN authorization.role_permissions rp ON r.id = rp.role_id
JOIN authorization.permissions p ON rp.permission_id = p.id
JOIN authorization.organizations o ON ur.organization_id = o.id
WHERE ur.is_active = true 
	AND r.is_active = true
	AND r.is_assignable = true
	AND o.is_active = true
	AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP);

-- Indexes on materialized view for performance
CREATE UNIQUE INDEX idx_user_perms_unique 
	ON authorization.user_effective_permissions(user_id, organization_id, permission, role_id);

CREATE INDEX idx_user_perms_user_org 
	ON authorization.user_effective_permissions(user_id, organization_id);

CREATE INDEX idx_user_perms_permission 
	ON authorization.user_effective_permissions(permission);

CREATE INDEX idx_user_perms_resource 
	ON authorization.user_effective_permissions(resource);

CREATE INDEX idx_user_perms_category 
	ON authorization.user_effective_permissions(permission_category);

CREATE INDEX idx_user_perms_path 
	ON authorization.user_effective_permissions USING GIST(org_path);

CREATE INDEX idx_user_perms_inheritable 
	ON authorization.user_effective_permissions(is_inheritable) 
	WHERE is_inheritable = true;

CREATE INDEX idx_user_perms_expires 
	ON authorization.user_effective_permissions(expires_at) 
	WHERE expires_at IS NOT NULL;

CREATE INDEX idx_user_perms_user_perm 
	ON authorization.user_effective_permissions(user_id, permission);

-- ============================================================================
-- Function: refresh_user_permissions
-- Description: Refreshes the materialized view (can be called manually or scheduled)
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.refresh_user_permissions()
RETURNS VOID AS $$
BEGIN
	REFRESH MATERIALIZED VIEW CONCURRENTLY authorization.user_effective_permissions;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: refresh_user_permissions_for_user
-- Description: Simulates refreshing permissions for a specific user
-- Note: PostgreSQL doesn't support partial materialized view refresh,
--       so this logs the need for refresh and the app should handle caching
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.refresh_user_permissions_for_user(
	p_user_id				UUID
)
RETURNS VOID AS $$
BEGIN
	-- Log that this user needs refresh
	PERFORM authorization.log_audit_event(
		'PERMISSION_REFRESH_REQUESTED',
		'SYSTEM',
		p_user_id,
		NULL,
		'MATERIALIZED_VIEW',
		NULL,
		'REFRESH_REQUEST',
		'success',
		jsonb_build_object(
			'user_id', p_user_id,
			'reason', 'User permissions changed'
		)
	);
	
	-- In a real implementation, this would trigger cache invalidation
	-- or incremental refresh if supported
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: get_permission_stats
-- Description: Returns statistics about the permission materialized view
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.get_permission_stats()
RETURNS TABLE(
	total_entries			BIGINT,
	unique_users			BIGINT,
	unique_organizations	BIGINT,
	unique_permissions		BIGINT,
	expired_entries			BIGINT,
	last_refresh			TIMESTAMP WITH TIME ZONE
) AS $$
BEGIN
	RETURN QUERY
	SELECT 
		COUNT(*) as total_entries,
		COUNT(DISTINCT user_id) as unique_users,
		COUNT(DISTINCT organization_id) as unique_organizations,
		COUNT(DISTINCT permission) as unique_permissions,
		COUNT(*) FILTER (WHERE expires_at IS NOT NULL AND expires_at <= CURRENT_TIMESTAMP) as expired_entries,
		(SELECT last_refresh FROM pg_stat_user_tables 
			WHERE schemaname = 'authorization' 
			AND tablename = 'user_effective_permissions') as last_refresh;
END;
$$ LANGUAGE plpgsql STABLE;

-- Comments
COMMENT ON MATERIALIZED VIEW authorization.user_effective_permissions IS 
	'Pre-computed view of all direct user permissions for performance optimization. Refreshed periodically.';

COMMENT ON FUNCTION authorization.refresh_user_permissions IS 
	'Performs a concurrent refresh of the user permissions materialized view';

COMMENT ON FUNCTION authorization.refresh_user_permissions_for_user IS 
	'Logs a request to refresh permissions for a specific user (actual refresh handled by application cache)';

COMMENT ON FUNCTION authorization.get_permission_stats IS 
	'Returns statistics about the permission materialized view for monitoring';