-- ============================================================================
-- File: 01_organization_management.sql
-- Description: Functions for managing organizations and hierarchies
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- ============================================================================
-- Function: create_organization
-- Description: Creates a new organization with automatic LTREE path generation
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.create_organization(
	p_name					VARCHAR(255),
	p_display_name			VARCHAR(255) DEFAULT NULL,
	p_description			TEXT DEFAULT NULL,
	p_parent_id				UUID DEFAULT NULL,
	p_created_by			UUID DEFAULT NULL,
	p_metadata				JSONB DEFAULT '{}'
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	path					LTREE,
	created_at				TIMESTAMP WITH TIME ZONE
) AS $$
DECLARE
	new_org_id				UUID;
	parent_path				LTREE;
	new_path				LTREE;
	safe_name				VARCHAR(255);
BEGIN
	-- Generate new UUID
	new_org_id := uuid_generate_v4();
	
	-- Create safe name for path (lowercase, replace special chars with underscore)
	safe_name := lower(regexp_replace(p_name, '[^a-zA-Z0-9]', '_', 'g'));
	
	-- Get parent path or create root path
	IF p_parent_id IS NOT NULL THEN
		SELECT o.path INTO parent_path
		FROM authorization.organizations o
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
	IF EXISTS (SELECT 1 FROM authorization.organizations WHERE path = new_path) THEN
		RAISE EXCEPTION 'Organization path already exists: %', new_path;
	END IF;
	
	-- Insert new organization
	INSERT INTO authorization.organizations (
		id, name, display_name, description, parent_id, path, created_by, metadata
	) VALUES (
		new_org_id, p_name, p_display_name, p_description, p_parent_id, new_path, p_created_by, p_metadata
	);
	
	-- Log creation event
	PERFORM authorization.log_audit_event(
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
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: move_organization
-- Description: Moves an organization to a new parent, updating all descendants
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.move_organization(
	p_org_id				UUID,
	p_new_parent_id			UUID,
	p_moved_by				UUID
)
RETURNS BOOLEAN AS $$
DECLARE
	old_path				LTREE;
	new_parent_path			LTREE;
	new_path				LTREE;
	org_name				VARCHAR(255);
	safe_name				VARCHAR(255);
	update_count			INTEGER;
BEGIN
	-- Get current organization path and name
	SELECT path, name INTO old_path, org_name
	FROM authorization.organizations
	WHERE id = p_org_id AND is_active = true;
	
	IF old_path IS NULL THEN
		RAISE EXCEPTION 'Organization not found: %', p_org_id;
	END IF;
	
	-- Create safe name for path
	safe_name := lower(regexp_replace(org_name, '[^a-zA-Z0-9]', '_', 'g'));
	
	-- Get new parent path
	IF p_new_parent_id IS NOT NULL THEN
		SELECT path INTO new_parent_path
		FROM authorization.organizations
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
	UPDATE authorization.organizations
	SET 
		path = new_path || subpath(path, nlevel(old_path)),
		parent_id = CASE WHEN id = p_org_id THEN p_new_parent_id ELSE parent_id END,
		updated_at = CURRENT_TIMESTAMP,
		updated_by = p_moved_by
	WHERE path <@ old_path;
	
	GET DIAGNOSTICS update_count = ROW_COUNT;
	
	-- Log move event
	PERFORM authorization.log_audit_event(
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
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: get_organization_hierarchy
-- Description: Returns organization hierarchy with user access information
-- ============================================================================
CREATE OR REPLACE FUNCTION authorization.get_organization_hierarchy(
	p_user_id				UUID DEFAULT NULL,
	p_root_org_id			UUID DEFAULT NULL,
	p_max_depth				INTEGER DEFAULT NULL,
	p_include_inactive		BOOLEAN DEFAULT false
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	display_name			VARCHAR,
	path					LTREE,
	level					INTEGER,
	parent_id				UUID,
	has_access				BOOLEAN,
	member_count			BIGINT,
	role_count				BIGINT,
	is_direct_member		BOOLEAN
) AS $$
DECLARE
	root_path				LTREE;
BEGIN
	-- Get root path if specified
	IF p_root_org_id IS NOT NULL THEN
		SELECT o.path INTO root_path
		FROM authorization.organizations o
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
			ELSE authorization.user_has_organization_access(p_user_id, o.id)
		END as has_access,
		(SELECT COUNT(*) FROM authorization.organization_memberships om 
			WHERE om.organization_id = o.id AND om.status = 'active') as member_count,
		(SELECT COUNT(*) FROM authorization.roles r 
			WHERE r.organization_id = o.id AND r.is_active = true) as role_count,
		CASE 
			WHEN p_user_id IS NULL THEN false
			ELSE EXISTS(
				SELECT 1 FROM authorization.organization_memberships om
				WHERE om.user_id = p_user_id 
					AND om.organization_id = o.id 
					AND om.status = 'active'
			)
		END as is_direct_member
	FROM authorization.organizations o
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
CREATE OR REPLACE FUNCTION authorization.user_has_organization_access(
	p_user_id				UUID,
	p_org_id				UUID
)
RETURNS BOOLEAN AS $$
DECLARE
	org_path				LTREE;
BEGIN
	-- Get organization path
	SELECT path INTO org_path
	FROM authorization.organizations
	WHERE id = p_org_id AND is_active = true;
	
	IF org_path IS NULL THEN
		RETURN false;
	END IF;
	
	-- Check for direct membership or role assignment
	RETURN EXISTS(
		SELECT 1 FROM authorization.organization_memberships om
		WHERE om.user_id = p_user_id 
			AND om.organization_id = p_org_id 
			AND om.status = 'active'
	) OR EXISTS(
		SELECT 1 FROM authorization.user_roles ur
		JOIN authorization.organizations o ON ur.organization_id = o.id
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
CREATE OR REPLACE FUNCTION authorization.get_user_organizations(
	p_user_id				UUID,
	p_include_inherited		BOOLEAN DEFAULT true
)
RETURNS TABLE(
	id						UUID,
	name					VARCHAR,
	display_name			VARCHAR,
	path					LTREE,
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
		(SELECT COUNT(*) FROM authorization.user_roles ur 
			WHERE ur.user_id = p_user_id 
			AND ur.organization_id = o.id 
			AND ur.is_active = true) as role_count
	FROM authorization.organizations o
	JOIN authorization.organization_memberships om ON o.id = om.organization_id
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
		FROM authorization.organizations o
		WHERE o.is_active = true
			AND EXISTS (
				SELECT 1 
				FROM authorization.user_roles ur
				JOIN authorization.roles r ON ur.role_id = r.id
				JOIN authorization.organizations parent_org ON ur.organization_id = parent_org.id
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
				FROM authorization.organization_memberships om2
				WHERE om2.user_id = p_user_id
					AND om2.organization_id = o.id
					AND om2.status = 'active'
			);
	END IF;
END;
$$ LANGUAGE plpgsql STABLE;

-- Comments
COMMENT ON FUNCTION authorization.create_organization IS 'Creates a new organization with automatic LTREE path generation';
COMMENT ON FUNCTION authorization.move_organization IS 'Moves an organization to a new parent, updating all descendant paths';
COMMENT ON FUNCTION authorization.get_organization_hierarchy IS 'Returns organization hierarchy tree with access and membership information';
COMMENT ON FUNCTION authorization.user_has_organization_access IS 'Checks if user has access to an organization through membership or inherited roles';
COMMENT ON FUNCTION authorization.get_user_organizations IS 'Returns all organizations a user has access to, either directly or through inheritance';