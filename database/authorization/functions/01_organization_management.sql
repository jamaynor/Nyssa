-- ============================================================================
-- File: 01_organization_management.sql
-- Description: Functions for managing organizations and hierarchies
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- ============================================================================
-- Function: ensure_admin_organization
-- Description: Ensures the Admin root organization exists
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.ensure_admin_organization()
RETURNS UUID AS $$
DECLARE
	admin_org_id			UUID := '00000000-0000-0000-0000-000000000001'::uuid;
	admin_exists			BOOLEAN;
BEGIN
	-- Check if Admin organization exists
	SELECT EXISTS(SELECT 1 FROM authz.organizations WHERE id = admin_org_id) INTO admin_exists;
	
	IF NOT admin_exists THEN
		-- Create the Admin root organization
		INSERT INTO authz.organizations (
			id,
			name,
			display_name,
			description,
			parent_id,
			path,
			metadata,
			settings,
			is_active,
			created_at,
			created_by
		) VALUES (
			admin_org_id,
			'Admin',
			'Administration',
			'Root administration organization for the RBAC system',
			NULL,
			'admin'::authz.ltree,
			'{"is_system_org": true, "is_root": true}'::jsonb,
			'{"allow_root_creation": false}'::jsonb,
			true,
			CURRENT_TIMESTAMP,
			admin_org_id
		);
		
		-- Log creation event
		PERFORM authz.log_audit_event(
			'ADMIN_ORGANIZATION_CREATED',
			'SYSTEM',
			admin_org_id,
			admin_org_id,
			'ORGANIZATION',
			admin_org_id,
			'CREATE',
			'success',
			jsonb_build_object('organization_name', 'Admin', 'path', 'admin', 'is_system_init', true)
		);
	END IF;
	
	RETURN admin_org_id;
END;
$$ LANGUAGE plpgsql SET search_path = authz, public;

-- ============================================================================
-- Function: create_organization
-- Description: Creates a new organization with automatic authz.ltree path generation
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.create_organization(
	p_name					VARCHAR(255),
	p_display_name			VARCHAR(255) DEFAULT NULL,
	p_description			TEXT DEFAULT NULL,
	p_parent_id				UUID DEFAULT NULL,
	p_created_by			UUID DEFAULT NULL,
	p_metadata				JSONB DEFAULT '{}',
	p_allow_root			BOOLEAN DEFAULT FALSE
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	path					authz.ltree,
	created_at				TIMESTAMP WITH TIME ZONE
) AS $$
DECLARE
	new_org_id				UUID;
	parent_path				authz.ltree;
	new_path				authz.ltree;
	safe_name				VARCHAR(255);
	admin_org_id			UUID := '00000000-0000-0000-0000-000000000001'::uuid;
BEGIN
	-- Ensure Admin organization exists (unless we're creating it)
	IF NOT p_allow_root OR p_name != 'Admin' THEN
		PERFORM authz.ensure_admin_organization();
	END IF;
	
	-- Generate new UUID
	new_org_id := authz.uuid_generate_v4();
	
	-- Create safe name for path (lowercase, replace special chars with underscore)
	safe_name := lower(regexp_replace(p_name, '[^a-zA-Z0-9]', '_', 'g'));
	
	-- Enforce Admin parent organization rule
	IF p_parent_id IS NULL AND NOT p_allow_root THEN
		-- If no parent specified and not explicitly allowing root creation, use Admin as parent
		p_parent_id := admin_org_id;
	END IF;
	
	-- Prevent creating organizations at root level (except Admin itself)
	IF p_parent_id IS NULL AND NOT p_allow_root THEN
		RAISE EXCEPTION 'All organizations must have Admin as root parent. Use Admin organization ID: %', admin_org_id;
	END IF;
	
	-- Get parent path or create root path
	IF p_parent_id IS NOT NULL THEN
		SELECT o.path INTO parent_path
		FROM authz.organizations o
		WHERE o.id = p_parent_id AND o.is_active = true;
		
		IF parent_path IS NULL THEN
			RAISE EXCEPTION 'Parent organization not found or inactive: %', p_parent_id;
		END IF;
		
		-- Create child path: parent.child
		new_path := parent_path || text2ltree(safe_name);
	ELSE
		-- Create root path
		new_path := text2ltree(safe_name);
	END IF;
	
	-- Check for path uniqueness
	IF EXISTS (SELECT 1 FROM authz.organizations o WHERE o.path = new_path) THEN
		RAISE EXCEPTION 'Organization path already exists: %', new_path;
	END IF;
	
	-- Insert new organization
	INSERT INTO authz.organizations (
		id, name, display_name, description, parent_id, path, created_by, metadata
	) VALUES (
		new_org_id, p_name, p_display_name, p_description, p_parent_id, new_path, p_created_by, p_metadata
	);
	
	-- Log creation event
	PERFORM authz.log_audit_event(
		'ORGANIZATION_CREATED',
		'ADMINISTRATION',
		p_created_by,
		new_org_id,
		'ORGANIZATION',
		new_org_id,
		'CREATE',
		'success',
		jsonb_build_object('organization_name', p_name, 'path', new_path::text)
	);
	
	-- Return created organization
	RETURN QUERY
	SELECT new_org_id, p_name, new_path, CURRENT_TIMESTAMP;
END;
$$ LANGUAGE plpgsql SET search_path = authz, public;

-- ============================================================================
-- Function: move_organization
-- Description: Moves an organization to a new parent, updating all descendants
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.move_organization(
	p_org_id				UUID,
	p_new_parent_id			UUID,
	p_moved_by				UUID
)
RETURNS BOOLEAN AS $$
DECLARE
	old_path				authz.ltree;
	new_parent_path			authz.ltree;
	new_path				authz.ltree;
	org_name				VARCHAR(255);
	safe_name				VARCHAR(255);
	update_count			INTEGER;
BEGIN
	-- Get current organization path and name
	SELECT path, name INTO old_path, org_name
	FROM authz.organizations
	WHERE id = p_org_id AND is_active = true;
	
	IF old_path IS NULL THEN
		RAISE EXCEPTION 'Organization not found: %', p_org_id;
	END IF;
	
	-- Create safe name for path
	safe_name := lower(regexp_replace(org_name, '[^a-zA-Z0-9]', '_', 'g'));
	
	-- Get new parent path
	IF p_new_parent_id IS NOT NULL THEN
		SELECT path INTO new_parent_path
		FROM authz.organizations
		WHERE id = p_new_parent_id AND is_active = true;
		
		IF new_parent_path IS NULL THEN
			RAISE EXCEPTION 'New parent organization not found: %', p_new_parent_id;
		END IF;
		
		-- Check for circular reference
		IF new_parent_path <@ old_path THEN
			RAISE EXCEPTION 'Cannot move organization to its own descendant';
		END IF;
		
		new_path := new_parent_path || text2ltree(safe_name);
	ELSE
		-- Moving to root
		new_path := text2ltree(safe_name);
	END IF;
	
	-- Update the organization and all its descendants
	UPDATE authz.organizations
	SET 
		path = CASE 
			WHEN id = p_org_id THEN new_path
			ELSE new_path || subpath(path, nlevel(old_path))
		END,
		parent_id = CASE WHEN id = p_org_id THEN p_new_parent_id ELSE parent_id END,
		updated_at = CURRENT_TIMESTAMP,
		updated_by = p_moved_by
	WHERE path <@ old_path;
	
	GET DIAGNOSTICS update_count = ROW_COUNT;
	
	-- Log move event
	PERFORM authz.log_audit_event(
		'ORGANIZATION_MOVED',
		'ADMINISTRATION',
		p_moved_by,
		p_org_id,
		'ORGANIZATION',
		p_org_id,
		'UPDATE',
		'success',
		jsonb_build_object(
			'old_path', old_path::text,
			'new_path', new_path::text,
			'new_parent_id', p_new_parent_id,
			'affected_count', update_count
		)
	);
	
	RETURN true;
END;
$$ LANGUAGE plpgsql SET search_path = authz, public;

-- ============================================================================
-- Function: get_organization_hierarchy
-- Description: Returns organization hierarchy with user access information
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_organization_hierarchy(
	p_user_id				UUID DEFAULT NULL,
	p_root_org_id			UUID DEFAULT NULL,
	p_max_depth				INTEGER DEFAULT NULL,
	p_include_inactive		BOOLEAN DEFAULT false
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	display_name			VARCHAR,
	path					authz.ltree,
	level					INTEGER,
	parent_id				UUID,
	has_access				BOOLEAN,
	member_count			BIGINT,
	role_count				BIGINT,
	is_direct_member		BOOLEAN
) AS $$
DECLARE
	root_path				authz.ltree;
BEGIN
	-- Get root path if specified
	IF p_root_org_id IS NOT NULL THEN
		SELECT o.path INTO root_path
		FROM authz.organizations o
		WHERE o.id = p_root_org_id
			AND (o.is_active = true OR p_include_inactive = true);
		
		IF root_path IS NULL THEN
			RAISE EXCEPTION 'Root organization not found: %', p_root_org_id;
		END IF;
	END IF;
	
	RETURN QUERY
	SELECT 
		o.id,
		o.name,
		o.display_name,
		o.path,
		nlevel(o.path) as level,
		o.parent_id,
		CASE 
			WHEN p_user_id IS NULL THEN true
			ELSE authz.user_has_organization_access(p_user_id, o.id)
		END as has_access,
		(SELECT COUNT(*) FROM authz.organization_memberships om 
			WHERE om.organization_id = o.id AND om.status = 'active') as member_count,
		(SELECT COUNT(*) FROM authz.roles r 
			WHERE r.organization_id = o.id AND r.is_active = true) as role_count,
		CASE 
			WHEN p_user_id IS NULL THEN false
			ELSE EXISTS(
				SELECT 1 FROM authz.organization_memberships om
				WHERE om.user_id = p_user_id 
					AND om.organization_id = o.id 
					AND om.status = 'active'
			)
		END as is_direct_member
	FROM authz.organizations o
	WHERE (o.is_active = true OR p_include_inactive = true)
		AND (root_path IS NULL OR o.path <@ root_path)
		AND (p_max_depth IS NULL OR nlevel(o.path) <= nlevel(COALESCE(root_path, ''::ltree)) + p_max_depth)
	ORDER BY o.path;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: user_has_organization_access
-- Description: Checks if user has access to organization (direct or inherited)
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.user_has_organization_access(
	p_user_id				UUID,
	p_org_id				UUID
)
RETURNS BOOLEAN AS $$
DECLARE
	org_path				authz.ltree;
BEGIN
	-- Get organization path
	SELECT path INTO org_path
	FROM authz.organizations
	WHERE id = p_org_id AND is_active = true;
	
	IF org_path IS NULL THEN
		RETURN false;
	END IF;
	
	-- Check for direct membership or role assignment
	RETURN EXISTS(
		SELECT 1 FROM authz.organization_memberships om
		WHERE om.user_id = p_user_id 
			AND om.organization_id = p_org_id 
			AND om.status = 'active'
	) OR EXISTS(
		SELECT 1 FROM authz.user_roles ur
		JOIN authz.organizations o ON ur.organization_id = o.id
		WHERE ur.user_id = p_user_id
			AND ur.is_active = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND o.path @> org_path  -- Ancestor organization
	);
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: get_user_organizations
-- Description: Returns all organizations a user has access to
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_user_organizations(
	p_user_id				UUID,
	p_include_inherited		BOOLEAN DEFAULT true
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	display_name			VARCHAR,
	path					authz.ltree,
	access_type				VARCHAR,  -- 'direct' or 'inherited'
	membership_type			VARCHAR,
	role_count				BIGINT
) AS $$
BEGIN
	-- Direct memberships
	RETURN QUERY
	SELECT 
		o.id,
		o.name,
		o.display_name,
		o.path,
		'direct'::VARCHAR as access_type,
		om.membership_type,
		(SELECT COUNT(*) FROM authz.user_roles ur 
			WHERE ur.user_id = p_user_id 
			AND ur.organization_id = o.id 
			AND ur.is_active = true) as role_count
	FROM authz.organizations o
	JOIN authz.organization_memberships om ON o.id = om.organization_id
	WHERE om.user_id = p_user_id
		AND om.status = 'active'
		AND o.is_active = true;
	
	-- Inherited access through parent organization roles
	IF p_include_inherited THEN
		RETURN QUERY
		SELECT DISTINCT
			o.id,
			o.name,
			o.display_name,
			o.path,
			'inherited'::VARCHAR as access_type,
			NULL::VARCHAR as membership_type,
			0::BIGINT as role_count
		FROM authz.organizations o
		WHERE o.is_active = true
			AND EXISTS (
				SELECT 1 
				FROM authz.user_roles ur
				JOIN authz.roles r ON ur.role_id = r.id
				JOIN authz.organizations parent_org ON ur.organization_id = parent_org.id
				WHERE ur.user_id = p_user_id
					AND ur.is_active = true
					AND r.is_inheritable = true
					AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
					AND parent_org.path @> o.path
					AND parent_org.id != o.id
			)
			AND NOT EXISTS (
				-- Exclude if user already has direct membership
				SELECT 1 
				FROM authz.organization_memberships om2
				WHERE om2.user_id = p_user_id
					AND om2.organization_id = o.id
					AND om2.status = 'active'
			);
	END IF;
END;
$$ LANGUAGE plpgsql STABLE;

-- Comments
COMMENT ON FUNCTION authz.ensure_admin_organization IS 'Ensures the Admin root organization exists in the system';
COMMENT ON FUNCTION authz.create_organization IS 'Creates a new organization with automatic authz.ltree path generation. All organizations descend from Admin unless p_allow_root is true';
COMMENT ON FUNCTION authz.move_organization IS 'Moves an organization to a new parent, updating all descendant paths';
COMMENT ON FUNCTION authz.get_organization_hierarchy IS 'Returns organization hierarchy tree with access and membership information';
COMMENT ON FUNCTION authz.user_has_organization_access IS 'Checks if user has access to an organization through membership or inherited roles';
COMMENT ON FUNCTION authz.get_user_organizations IS 'Returns all organizations a user has access to, either directly or through inheritance';