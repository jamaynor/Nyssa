-- ============================================================================
-- File: 07_user_roles.sql
-- Description: User roles table for assigning roles to users within organizations
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authorization.user_roles CASCADE;

CREATE TABLE authorization.user_roles (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	user_id					UUID NOT NULL REFERENCES authorization.users(id) ON DELETE CASCADE,
	role_id					UUID NOT NULL REFERENCES authorization.roles(id) ON DELETE CASCADE,
	organization_id			UUID NOT NULL REFERENCES authorization.organizations(id) ON DELETE CASCADE,
	granted_by				UUID REFERENCES authorization.users(id),
	granted_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	expires_at				TIMESTAMP WITH TIME ZONE,
	conditions				JSONB DEFAULT '{}',
	metadata				JSONB DEFAULT '{}',
	is_active				BOOLEAN DEFAULT true,
	updated_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	
	-- Constraints
	CONSTRAINT user_roles_unique			UNIQUE(user_id, role_id, organization_id),
	CONSTRAINT user_roles_expiry_future		CHECK(expires_at IS NULL OR expires_at > granted_at)
);

-- Indexes for performance
CREATE INDEX idx_user_roles_user_id			ON authorization.user_roles(user_id);
CREATE INDEX idx_user_roles_org_id			ON authorization.user_roles(organization_id);
CREATE INDEX idx_user_roles_role_id			ON authorization.user_roles(role_id);
CREATE INDEX idx_user_roles_granted_by		ON authorization.user_roles(granted_by);
CREATE INDEX idx_user_roles_active			ON authorization.user_roles(user_id, organization_id, is_active) 
	WHERE is_active = true;
CREATE INDEX idx_user_roles_expiry			ON authorization.user_roles(expires_at) 
	WHERE expires_at IS NOT NULL;
CREATE INDEX idx_user_roles_conditions		ON authorization.user_roles USING GIN(conditions);
CREATE INDEX idx_user_roles_granted_at		ON authorization.user_roles(granted_at);

-- Comments
COMMENT ON TABLE authorization.user_roles IS 'Assigns roles to users within specific organizations';
COMMENT ON COLUMN authorization.user_roles.id IS 'Unique assignment identifier';
COMMENT ON COLUMN authorization.user_roles.user_id IS 'User receiving the role';
COMMENT ON COLUMN authorization.user_roles.role_id IS 'Role being assigned';
COMMENT ON COLUMN authorization.user_roles.organization_id IS 'Organization context for this role assignment';
COMMENT ON COLUMN authorization.user_roles.granted_by IS 'User who made this assignment';
COMMENT ON COLUMN authorization.user_roles.granted_at IS 'When the role was assigned';
COMMENT ON COLUMN authorization.user_roles.expires_at IS 'Optional expiration time for temporary roles';
COMMENT ON COLUMN authorization.user_roles.conditions IS 'Additional conditions (e.g., IP restrictions, time windows)';
COMMENT ON COLUMN authorization.user_roles.metadata IS 'Assignment metadata (e.g., approval workflow data)';
COMMENT ON COLUMN authorization.user_roles.is_active IS 'Whether this assignment is currently active';

-- Trigger to update updated_at timestamp
CREATE TRIGGER update_user_roles_updated_at 
	BEFORE UPDATE ON authorization.user_roles
	FOR EACH ROW 
	EXECUTE FUNCTION authorization.update_updated_at_column();