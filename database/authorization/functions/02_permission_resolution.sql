-- ============================================================================
-- File: 02_permission_resolution.sql
-- Description: Functions for resolving and checking user permissions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- ============================================================================
-- Function: resolve_user_permissions
-- Description: Resolves all permissions for a user in an organization with inheritance
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.resolve_user_permissions(
	p_user_id				UUID, 
	p_org_id				UUID,
	p_include_inherited		BOOLEAN DEFAULT true,
	p_permission_filter		VARCHAR DEFAULT NULL
)
RETURNS TABLE(
	permission				VARCHAR, 
	permission_source		VARCHAR, 
	source_org_name			VARCHAR, 
	source_org_path			authz.ltree,
	role_name				VARCHAR,
	role_id					UUID,
	role_priority			INTEGER,
	granted_at				TIMESTAMP WITH TIME ZONE,
	expires_at				TIMESTAMP WITH TIME ZONE,
	is_dangerous			BOOLEAN
) AS $$
DECLARE
	org_path				authz.ltree;
	permission_pattern		VARCHAR;
BEGIN
	-- Get the organization's path
	SELECT o.path INTO org_path 
	FROM authz.organizations o 
	WHERE o.id = p_org_id AND o.is_active = true;
	
	IF org_path IS NULL THEN
		RAISE EXCEPTION 'Organization not found or inactive: %', p_org_id;
	END IF;
	
	-- Prepare permission filter pattern
	permission_pattern := COALESCE(p_permission_filter || '%', '%');
	
	-- Direct permissions in target organization
	RETURN QUERY
	SELECT 
		p.permission,
		'direct'::VARCHAR as permission_source,
		o.name as source_org_name,
		o.path as source_org_path,
		r.name as role_name,
		r.id as role_id,
		r.priority as role_priority,
		ur.granted_at,
		ur.expires_at,
		p.is_dangerous
	FROM authz.user_roles ur
	JOIN authz.roles r ON ur.role_id = r.id
	JOIN authz.role_permissions rp ON r.id = rp.role_id
	JOIN authz.permissions p ON rp.permission_id = p.id
	JOIN authz.organizations o ON ur.organization_id = o.id
	WHERE ur.user_id = p_user_id 
		AND ur.organization_id = p_org_id
		AND ur.is_active = true
		AND r.is_active = true
		AND r.is_assignable = true
		AND o.is_active = true
		AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
		AND p.permission LIKE permission_pattern;
	
	-- Inherited permissions from ancestor orgs (if enabled)
	IF p_include_inherited THEN
		RETURN QUERY
		SELECT DISTINCT
			p.permission,
			'inherited'::VARCHAR as permission_source,
			o.name as source_org_name,
			o.path as source_org_path,
			r.name as role_name,
			r.id as role_id,
			r.priority as role_priority,
			ur.granted_at,
			ur.expires_at,
			p.is_dangerous
		FROM authz.user_roles ur
		JOIN authz.roles r ON ur.role_id = r.id AND r.is_inheritable = true
		JOIN authz.role_permissions rp ON r.id = rp.role_id
		JOIN authz.permissions p ON rp.permission_id = p.id
		JOIN authz.organizations o ON ur.organization_id = o.id
		WHERE ur.user_id = p_user_id 
			AND o.path @> org_path  -- Ancestor organization
			AND o.path != org_path  -- Exclude self
			AND ur.is_active = true
			AND r.is_active = true
			AND r.is_assignable = true
			AND o.is_active = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND p.permission LIKE permission_pattern;
	END IF;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: check_user_permission
-- Description: Optimized single permission check with inheritance support
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.check_user_permission(
	p_user_id				UUID,
	p_org_id				UUID,
	p_permission			VARCHAR,
	p_audit					BOOLEAN DEFAULT true
)
RETURNS TABLE(
	allowed					BOOLEAN,
	source					VARCHAR,
	role_name				VARCHAR,
	expires_at				TIMESTAMP WITH TIME ZONE
) AS $$
DECLARE
	org_path				authz.ltree;
	result_record			RECORD;
BEGIN
	-- Get organization path
	SELECT o.path INTO org_path
	FROM authz.organizations o
	WHERE o.id = p_org_id AND o.is_active = true;
	
	IF org_path IS NULL THEN
		RETURN QUERY SELECT false, 'organization_not_found'::VARCHAR, NULL::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE;
		RETURN;
	END IF;
	
	-- Check direct permissions first (fastest path)
	SELECT 
		true as allowed,
		'direct'::VARCHAR as source,
		r.name as role_name,
		ur.expires_at
	INTO result_record
	FROM authz.user_roles ur
	JOIN authz.roles r ON ur.role_id = r.id
	JOIN authz.role_permissions rp ON r.id = rp.role_id
	JOIN authz.permissions p ON rp.permission_id = p.id
	WHERE ur.user_id = p_user_id 
		AND ur.organization_id = p_org_id
		AND ur.is_active = true
		AND r.is_active = true
		AND r.is_assignable = true
		AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
		AND p.permission = p_permission
	LIMIT 1;
	
	-- If found directly, return immediately
	IF FOUND THEN
		-- Log audit event if enabled
		IF p_audit THEN
			PERFORM authz.log_audit_event(
				'PERMISSION_CHECK',
				'SECURITY',
				p_user_id,
				p_org_id,
				'PERMISSION',
				NULL,
				'CHECK',
				'success',
				jsonb_build_object(
					'permission', p_permission,
					'result', 'allowed',
					'source', result_record.source
				)
			);
		END IF;
		RETURN QUERY SELECT result_record.allowed, result_record.source, result_record.role_name, result_record.expires_at;
		RETURN;
	END IF;
	
	-- Check inherited permissions
	BEGIN
		SELECT 
			true as allowed,
			'inherited'::VARCHAR as source,
			r.name as role_name,
			ur.expires_at
		INTO result_record
		FROM authz.user_roles ur
		JOIN authz.roles r ON ur.role_id = r.id AND r.is_inheritable = true
		JOIN authz.role_permissions rp ON r.id = rp.role_id
		JOIN authz.permissions p ON rp.permission_id = p.id
		JOIN authz.organizations o ON ur.organization_id = o.id
		WHERE ur.user_id = p_user_id 
			AND o.path @> org_path
			AND o.path != org_path
			AND ur.is_active = true
			AND r.is_active = true
			AND r.is_assignable = true
			AND o.is_active = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND p.permission = p_permission
		ORDER BY r.priority DESC  -- Higher priority wins
		LIMIT 1;
		
		IF FOUND THEN
			-- Log audit event if enabled
			IF p_audit THEN
				PERFORM authz.log_audit_event(
					'PERMISSION_CHECK',
					'SECURITY',
					p_user_id,
					p_org_id,
					'PERMISSION',
					NULL,
					'CHECK',
					'success',
					jsonb_build_object(
						'permission', p_permission,
						'result', 'allowed',
						'source', result_record.source
					)
				);
			END IF;
			RETURN QUERY SELECT result_record.allowed, result_record.source, result_record.role_name, result_record.expires_at;
			RETURN;
		END IF;
	END;
	
	-- No permission found
	IF p_audit THEN
		PERFORM authz.log_audit_event(
			'PERMISSION_CHECK',
			'SECURITY',
			p_user_id,
			p_org_id,
			'PERMISSION',
			NULL,
			'CHECK',
			'success',
			jsonb_build_object(
				'permission', p_permission,
				'result', 'denied',
				'source', 'permission_denied'
			)
		);
	END IF;
	RETURN QUERY SELECT false, 'permission_denied'::VARCHAR, NULL::VARCHAR, NULL::TIMESTAMP WITH TIME ZONE;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: check_user_permissions_bulk
-- Description: Efficiently check multiple permissions at once
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.check_user_permissions_bulk(
	p_user_id				UUID,
	p_org_id				UUID,
	p_permissions			VARCHAR[],
	p_audit					BOOLEAN DEFAULT true
)
RETURNS TABLE(
	permission				VARCHAR,
	has_permission			BOOLEAN,
	source					VARCHAR,
	role_name				VARCHAR
) AS $$
DECLARE
	org_path				authz.ltree;
	perm					VARCHAR;
BEGIN
	-- Get organization path
	SELECT o.path INTO org_path
	FROM authz.organizations o
	WHERE o.id = p_org_id AND o.is_active = true;
	
	IF org_path IS NULL THEN
		-- Return all permissions as denied
		FOREACH perm IN ARRAY p_permissions LOOP
			RETURN QUERY SELECT perm, false, 'organization_not_found'::VARCHAR, NULL::VARCHAR;
		END LOOP;
		RETURN;
	END IF;
	
	-- Check all permissions in one query
	RETURN QUERY
	WITH user_permissions AS (
		-- Direct permissions
		SELECT DISTINCT ON (p.permission)
			p.permission,
			'direct'::VARCHAR as source,
			r.name as role_name,
			r.priority
		FROM authz.user_roles ur
		JOIN authz.roles r ON ur.role_id = r.id
		JOIN authz.role_permissions rp ON r.id = rp.role_id
		JOIN authz.permissions p ON rp.permission_id = p.id
		WHERE ur.user_id = p_user_id 
			AND ur.organization_id = p_org_id
			AND ur.is_active = true
			AND r.is_active = true
			AND r.is_assignable = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND p.permission = ANY(p_permissions)
		
		UNION ALL
		
		-- Inherited permissions
		SELECT DISTINCT ON (p.permission)
			p.permission,
			'inherited'::VARCHAR as source,
			r.name as role_name,
			r.priority
		FROM authz.user_roles ur
		JOIN authz.roles r ON ur.role_id = r.id AND r.is_inheritable = true
		JOIN authz.role_permissions rp ON r.id = rp.role_id
		JOIN authz.permissions p ON rp.permission_id = p.id
		JOIN authz.organizations o ON ur.organization_id = o.id
		WHERE ur.user_id = p_user_id 
			AND o.path @> org_path
			AND o.path != org_path
			AND ur.is_active = true
			AND r.is_active = true
			AND r.is_assignable = true
			AND o.is_active = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND p.permission = ANY(p_permissions)
	)
	-- Get highest priority permission for each permission string
	, prioritized_permissions AS (
		SELECT DISTINCT ON (user_permissions.permission)
			user_permissions.permission,
			user_permissions.source,
			user_permissions.role_name
		FROM user_permissions
		ORDER BY user_permissions.permission, 
			CASE user_permissions.source WHEN 'direct' THEN 1 ELSE 2 END,  -- Direct beats inherited
			priority DESC
	)
	-- Return results for all requested permissions
	SELECT 
		perm.permission,
		COALESCE(pp.permission IS NOT NULL, false) as has_permission,
		COALESCE(pp.source, 'permission_denied'::VARCHAR) as source,
		pp.role_name
	FROM unnest(p_permissions) AS perm(permission)
	LEFT JOIN prioritized_permissions pp ON pp.permission = perm.permission;
	
	-- Log audit event if enabled
	IF p_audit THEN
		PERFORM authz.log_audit_event(
			'BULK_PERMISSION_CHECK',
			'SECURITY',
			p_user_id,
			p_org_id,
			'PERMISSIONS',
			NULL,
			'CHECK',
			'success',
			jsonb_build_object(
				'permissions_checked', array_length(p_permissions, 1),
				'permissions', p_permissions
			)
		);
	END IF;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: get_role_permissions
-- Description: Get all permissions for a specific role
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_role_permissions(
	p_role_id				UUID
)
RETURNS TABLE(
	permission				VARCHAR,
	display_name			VARCHAR,
	description				TEXT,
	category				VARCHAR,
	resource				VARCHAR,
	action					VARCHAR,
	is_dangerous			BOOLEAN,
	granted_at				TIMESTAMP WITH TIME ZONE,
	granted_by				UUID
) AS $$
BEGIN
	RETURN QUERY
	SELECT 
		p.permission,
		p.display_name,
		p.description,
		p.category,
		p.resource,
		p.action,
		p.is_dangerous,
		rp.granted_at,
		rp.granted_by
	FROM authz.role_permissions rp
	JOIN authz.permissions p ON rp.permission_id = p.id
	WHERE rp.role_id = p_role_id
	ORDER BY p.category, p.resource, p.action;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: get_effective_permissions_summary
-- Description: Get a summary of all effective permissions for a user in an org
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_effective_permissions_summary(
	p_user_id				UUID,
	p_org_id				UUID
)
RETURNS TABLE(
	category				VARCHAR,
	resource				VARCHAR,
	permissions				TEXT[],
	source					VARCHAR,
	role_names				TEXT[]
) AS $$
BEGIN
	RETURN QUERY
	WITH all_permissions AS (
		SELECT * FROM authz.resolve_user_permissions(p_user_id, p_org_id, true, NULL)
	)
	SELECT 
		p.category,
		p.resource,
		array_agg(DISTINCT p.action ORDER BY p.action) as permissions,
		ap.source,
		array_agg(DISTINCT ap.role_name ORDER BY ap.role_name) as role_names
	FROM all_permissions ap
	JOIN authz.permissions p ON ap.permission = p.permission
	GROUP BY p.category, p.resource, ap.source
	ORDER BY p.category, p.resource, ap.source;
END;
$$ LANGUAGE plpgsql STABLE;

-- Comments
COMMENT ON FUNCTION authz.resolve_user_permissions IS 'Resolves all permissions for a user in an organization, including inherited permissions';
COMMENT ON FUNCTION authz.check_user_permission IS 'Optimized function to check if a user has a specific permission in an organization';
COMMENT ON FUNCTION authz.check_user_permissions_bulk IS 'Efficiently checks multiple permissions at once for a user';
COMMENT ON FUNCTION authz.get_role_permissions IS 'Returns all permissions assigned to a specific role';
COMMENT ON FUNCTION authz.get_effective_permissions_summary IS 'Returns a categorized summary of all effective permissions for a user';