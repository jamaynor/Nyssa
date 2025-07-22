-- ============================================================================
-- File: 03_role_management.sql
-- Description: Functions for managing role assignments and permissions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- ============================================================================
-- Function: assign_user_role
-- Description: Assigns a role to a user with full validation
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.assign_user_role(
	p_user_id				UUID,
	p_role_id				UUID,
	p_organization_id		UUID,
	p_granted_by			UUID,
	p_expires_at			TIMESTAMP WITH TIME ZONE DEFAULT NULL,
	p_conditions			JSONB DEFAULT '{}',
	p_metadata				JSONB DEFAULT '{}'
)
RETURNS TABLE(
	assignment_id			UUID,
	assigned_at				TIMESTAMP WITH TIME ZONE
) AS $$
DECLARE
	new_assignment_id		UUID;
	role_record				RECORD;
	user_record				RECORD;
	org_record				RECORD;
BEGIN
	-- Validate user exists and is active
	SELECT id, email, is_active INTO user_record
	FROM authz.users
	WHERE id = p_user_id;
	
	IF NOT FOUND OR NOT user_record.is_active THEN
		RAISE EXCEPTION 'User not found or inactive: %', p_user_id;
	END IF;
	
	-- Validate role exists and is assignable
	SELECT id, name, organization_id, is_active, is_assignable INTO role_record
	FROM authz.roles
	WHERE id = p_role_id;
	
	IF NOT FOUND OR NOT role_record.is_active OR NOT role_record.is_assignable THEN
		RAISE EXCEPTION 'Role not found, inactive, or not assignable: %', p_role_id;
	END IF;
	
	-- Validate role belongs to specified organization
	IF role_record.organization_id != p_organization_id THEN
		RAISE EXCEPTION 'Role does not belong to specified organization';
	END IF;
	
	-- Validate organization exists and is active
	SELECT id, name, is_active INTO org_record
	FROM authz.organizations
	WHERE id = p_organization_id;
	
	IF NOT FOUND OR NOT org_record.is_active THEN
		RAISE EXCEPTION 'Organization not found or inactive: %', p_organization_id;
	END IF;
	
	-- Check if user is member of organization
	IF NOT EXISTS(
		SELECT 1 FROM authz.organization_memberships
		WHERE user_id = p_user_id 
			AND organization_id = p_organization_id 
			AND status = 'active'
	) THEN
		RAISE EXCEPTION 'User is not a member of the organization';
	END IF;
	
	-- Check for existing active assignment
	IF EXISTS(
		SELECT 1 FROM authz.user_roles
		WHERE user_id = p_user_id 
			AND role_id = p_role_id 
			AND organization_id = p_organization_id
			AND is_active = true
	) THEN
		RAISE EXCEPTION 'User already has this role in the organization';
	END IF;
	
	-- Validate expiration date
	IF p_expires_at IS NOT NULL AND p_expires_at <= CURRENT_TIMESTAMP THEN
		RAISE EXCEPTION 'Expiration date must be in the future';
	END IF;
	
	-- Create the assignment
	new_assignment_id := uuid_generate_v4();
	
	INSERT INTO authz.user_roles (
		id, user_id, role_id, organization_id, granted_by, 
		expires_at, conditions, metadata
	) VALUES (
		new_assignment_id, p_user_id, p_role_id, p_organization_id, p_granted_by,
		p_expires_at, p_conditions, p_metadata
	);
	
	-- Log the assignment
	PERFORM authz.log_audit_event(
		'ROLE_ASSIGNED',
		'AUTHORIZATION',
		p_granted_by,
		p_organization_id,
		'USER_ROLE',
		new_assignment_id,
		'CREATE',
		'success',
		jsonb_build_object(
			'user_id', p_user_id,
			'user_email', user_record.email,
			'role_id', p_role_id,
			'role_name', role_record.name,
			'organization_id', p_organization_id,
			'organization_name', org_record.name,
			'expires_at', p_expires_at
		)
	);
	
	-- Invalidate user permission cache
	PERFORM authz.invalidate_user_permission_cache(p_user_id, p_organization_id);
	
	RETURN QUERY SELECT new_assignment_id, CURRENT_TIMESTAMP;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: revoke_user_role
-- Description: Revokes a role from a user
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.revoke_user_role(
	p_user_id				UUID,
	p_role_id				UUID,
	p_organization_id		UUID,
	p_revoked_by			UUID,
	p_reason				VARCHAR DEFAULT NULL
)
RETURNS BOOLEAN AS $$
DECLARE
	assignment_record		RECORD;
	role_name				VARCHAR;
	org_name				VARCHAR;
BEGIN
	-- Find and validate the assignment
	SELECT id, granted_at, granted_by INTO assignment_record
	FROM authz.user_roles
	WHERE user_id = p_user_id 
		AND role_id = p_role_id 
		AND organization_id = p_organization_id
		AND is_active = true;
	
	IF NOT FOUND THEN
		RAISE EXCEPTION 'Role assignment not found or already revoked';
	END IF;
	
	-- Get role and org names for logging
	SELECT name INTO role_name FROM authz.roles WHERE id = p_role_id;
	SELECT name INTO org_name FROM authz.organizations WHERE id = p_organization_id;
	
	-- Deactivate the assignment
	UPDATE authz.user_roles
	SET 
		is_active = false,
		updated_at = CURRENT_TIMESTAMP,
		metadata = metadata || jsonb_build_object(
			'revoked_at', CURRENT_TIMESTAMP,
			'revoked_by', p_revoked_by,
			'revocation_reason', p_reason
		)
	WHERE id = assignment_record.id;
	
	-- Log the revocation
	PERFORM authz.log_audit_event(
		'ROLE_REVOKED',
		'AUTHORIZATION',
		p_revoked_by,
		p_organization_id,
		'USER_ROLE',
		assignment_record.id,
		'DELETE',
		'success',
		jsonb_build_object(
			'user_id', p_user_id,
			'role_id', p_role_id,
			'role_name', role_name,
			'organization_id', p_organization_id,
			'organization_name', org_name,
			'revocation_reason', p_reason,
			'original_granted_at', assignment_record.granted_at,
			'original_granted_by', assignment_record.granted_by
		)
	);
	
	-- Invalidate user permission cache
	PERFORM authz.invalidate_user_permission_cache(p_user_id, p_organization_id);
	
	RETURN true;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: get_user_roles
-- Description: Gets all roles assigned to a user in an organization
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_user_roles(
	p_user_id				UUID,
	p_organization_id		UUID DEFAULT NULL,
	p_include_inherited		BOOLEAN DEFAULT true
)
RETURNS TABLE(
	role_id					UUID,
	role_name				VARCHAR,
	role_display_name		VARCHAR,
	organization_id			UUID,
	organization_name		VARCHAR,
	granted_at				TIMESTAMP WITH TIME ZONE,
	expires_at				TIMESTAMP WITH TIME ZONE,
	granted_by				UUID,
	is_inheritable			BOOLEAN,
	source					VARCHAR  -- 'direct' or 'inherited'
) AS $$
BEGIN
	-- Direct role assignments
	RETURN QUERY
	SELECT 
		r.id as role_id,
		r.name as role_name,
		r.display_name as role_display_name,
		ur.organization_id,
		o.name as organization_name,
		ur.granted_at,
		ur.expires_at,
		ur.granted_by,
		r.is_inheritable,
		'direct'::VARCHAR as source
	FROM authz.user_roles ur
	JOIN authz.roles r ON ur.role_id = r.id
	JOIN authz.organizations o ON ur.organization_id = o.id
	WHERE ur.user_id = p_user_id
		AND ur.is_active = true
		AND r.is_active = true
		AND o.is_active = true
		AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
		AND (p_organization_id IS NULL OR ur.organization_id = p_organization_id);
	
	-- Inherited roles from parent organizations
	IF p_include_inherited AND p_organization_id IS NOT NULL THEN
		RETURN QUERY
		SELECT 
			r.id as role_id,
			r.name as role_name,
			r.display_name as role_display_name,
			ur.organization_id,
			o.name as organization_name,
			ur.granted_at,
			ur.expires_at,
			ur.granted_by,
			r.is_inheritable,
			'inherited'::VARCHAR as source
		FROM authz.user_roles ur
		JOIN authz.roles r ON ur.role_id = r.id AND r.is_inheritable = true
		JOIN authz.organizations o ON ur.organization_id = o.id
		JOIN authz.organizations target_org ON target_org.id = p_organization_id
		WHERE ur.user_id = p_user_id
			AND ur.is_active = true
			AND r.is_active = true
			AND o.is_active = true
			AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)
			AND o.path @> target_org.path  -- Parent organization
			AND o.id != p_organization_id;  -- Exclude direct assignments
	END IF;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: add_permission_to_role
-- Description: Adds a permission to a role
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.add_permission_to_role(
	p_role_id				UUID,
	p_permission			VARCHAR,
	p_granted_by			UUID,
	p_conditions			JSONB DEFAULT '{}',
	p_metadata				JSONB DEFAULT '{}'
)
RETURNS BOOLEAN AS $$
DECLARE
	permission_id			UUID;
	role_record				RECORD;
	assignment_id			UUID;
BEGIN
	-- Get permission ID
	SELECT id INTO permission_id
	FROM authz.permissions
	WHERE permission = p_permission;
	
	IF NOT FOUND THEN
		RAISE EXCEPTION 'Permission not found: %', p_permission;
	END IF;
	
	-- Validate role exists and is active
	SELECT id, name, organization_id, is_active INTO role_record
	FROM authz.roles
	WHERE id = p_role_id;
	
	IF NOT FOUND OR NOT role_record.is_active THEN
		RAISE EXCEPTION 'Role not found or inactive: %', p_role_id;
	END IF;
	
	-- Check if permission already assigned
	IF EXISTS(
		SELECT 1 FROM authz.role_permissions
		WHERE role_id = p_role_id AND permission_id = permission_id
	) THEN
		RAISE EXCEPTION 'Permission already assigned to role';
	END IF;
	
	-- Create assignment
	assignment_id := uuid_generate_v4();
	
	INSERT INTO authz.role_permissions (
		id, role_id, permission_id, granted_by, conditions, metadata
	) VALUES (
		assignment_id, p_role_id, permission_id, p_granted_by, p_conditions, p_metadata
	);
	
	-- Log the permission grant
	PERFORM authz.log_audit_event(
		'PERMISSION_GRANTED_TO_ROLE',
		'AUTHORIZATION',
		p_granted_by,
		role_record.organization_id,
		'ROLE_PERMISSION',
		assignment_id,
		'CREATE',
		'success',
		jsonb_build_object(
			'role_id', p_role_id,
			'role_name', role_record.name,
			'permission', p_permission,
			'organization_id', role_record.organization_id
		)
	);
	
	RETURN true;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: remove_permission_from_role
-- Description: Removes a permission from a role
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.remove_permission_from_role(
	p_role_id				UUID,
	p_permission			VARCHAR,
	p_removed_by			UUID
)
RETURNS BOOLEAN AS $$
DECLARE
	permission_id			UUID;
	role_record				RECORD;
	assignment_id			UUID;
BEGIN
	-- Get permission ID
	SELECT id INTO permission_id
	FROM authz.permissions
	WHERE permission = p_permission;
	
	IF NOT FOUND THEN
		RAISE EXCEPTION 'Permission not found: %', p_permission;
	END IF;
	
	-- Get role info
	SELECT id, name, organization_id INTO role_record
	FROM authz.roles
	WHERE id = p_role_id;
	
	IF NOT FOUND THEN
		RAISE EXCEPTION 'Role not found: %', p_role_id;
	END IF;
	
	-- Get assignment ID for logging
	SELECT id INTO assignment_id
	FROM authz.role_permissions
	WHERE role_id = p_role_id AND permission_id = permission_id;
	
	IF NOT FOUND THEN
		RAISE EXCEPTION 'Permission not assigned to role';
	END IF;
	
	-- Remove the permission
	DELETE FROM authz.role_permissions
	WHERE role_id = p_role_id AND permission_id = permission_id;
	
	-- Log the permission removal
	PERFORM authz.log_audit_event(
		'PERMISSION_REMOVED_FROM_ROLE',
		'AUTHORIZATION',
		p_removed_by,
		role_record.organization_id,
		'ROLE_PERMISSION',
		assignment_id,
		'DELETE',
		'success',
		jsonb_build_object(
			'role_id', p_role_id,
			'role_name', role_record.name,
			'permission', p_permission,
			'organization_id', role_record.organization_id
		)
	);
	
	RETURN true;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: expire_user_roles
-- Description: Expires user roles that have passed their expiration date
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.expire_user_roles()
RETURNS TABLE(
	expired_count			INTEGER,
	user_ids				UUID[]
) AS $$
DECLARE
	affected_users			UUID[];
	expired_roles			INTEGER;
BEGIN
	-- Deactivate expired roles
	UPDATE authz.user_roles
	SET 
		is_active = false,
		metadata = metadata || jsonb_build_object(
			'auto_expired_at', CURRENT_TIMESTAMP,
			'expiration_reason', 'Scheduled expiration'
		)
	WHERE is_active = true
		AND expires_at IS NOT NULL
		AND expires_at <= CURRENT_TIMESTAMP
	RETURNING user_id INTO affected_users;
	
	GET DIAGNOSTICS expired_roles = ROW_COUNT;
	
	-- Log expiration event if any roles were expired
	IF expired_roles > 0 THEN
		PERFORM authz.log_audit_event(
			'ROLES_EXPIRED',
			'SYSTEM',
			NULL,
			NULL,
			'USER_ROLE',
			NULL,
			'EXPIRE',
			'success',
			jsonb_build_object(
				'expired_count', expired_roles,
				'affected_users', array_length(affected_users, 1)
			)
		);
	END IF;
	
	RETURN QUERY SELECT expired_roles, array_agg(DISTINCT unnest) FROM unnest(affected_users);
END;
$$ LANGUAGE plpgsql;

-- Comments
COMMENT ON FUNCTION authz.assign_user_role IS 'Assigns a role to a user with comprehensive validation and audit logging';
COMMENT ON FUNCTION authz.revoke_user_role IS 'Revokes a role from a user and logs the revocation';
COMMENT ON FUNCTION authz.get_user_roles IS 'Returns all roles assigned to a user, including inherited roles from parent organizations';
COMMENT ON FUNCTION authz.add_permission_to_role IS 'Adds a permission to a role with validation';
COMMENT ON FUNCTION authz.remove_permission_from_role IS 'Removes a permission from a role';
COMMENT ON FUNCTION authz.expire_user_roles IS 'Automatically expires user roles that have passed their expiration date';