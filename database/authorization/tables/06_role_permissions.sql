-- ============================================================================
-- File: 06_role_permissions.sql
-- Description: Role permissions junction table for role-permission relationships
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authz.role_permissions CASCADE;

CREATE TABLE authz.role_permissions (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	role_id					UUID NOT NULL REFERENCES authz.roles(id) ON DELETE CASCADE,
	permission_id			UUID NOT NULL REFERENCES authz.permissions(id) ON DELETE CASCADE,
	granted_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	granted_by				UUID REFERENCES authz.users(id),
	conditions				JSONB DEFAULT '{}',
	metadata				JSONB DEFAULT '{}',
	
	-- Constraints
	CONSTRAINT role_permissions_unique		UNIQUE(role_id, permission_id)
);

-- Indexes for performance
CREATE INDEX idx_role_permissions_role_id		ON authz.role_permissions(role_id);
CREATE INDEX idx_role_permissions_permission_id	ON authz.role_permissions(permission_id);
CREATE INDEX idx_role_permissions_granted_at	ON authz.role_permissions(granted_at);
CREATE INDEX idx_role_permissions_granted_by	ON authz.role_permissions(granted_by);
CREATE INDEX idx_role_permissions_conditions	ON authz.role_permissions USING GIN(conditions);

-- Comments
COMMENT ON TABLE authz.role_permissions IS 'Maps permissions to roles';
COMMENT ON COLUMN authz.role_permissions.id IS 'Unique assignment identifier';
COMMENT ON COLUMN authz.role_permissions.role_id IS 'Role receiving the permission';
COMMENT ON COLUMN authz.role_permissions.permission_id IS 'Permission being granted to the role';
COMMENT ON COLUMN authz.role_permissions.granted_at IS 'When the permission was added to the role';
COMMENT ON COLUMN authz.role_permissions.granted_by IS 'User who granted this permission to the role';
COMMENT ON COLUMN authz.role_permissions.conditions IS 'Optional conditions for this permission (e.g., time-based, resource-specific)';
COMMENT ON COLUMN authz.role_permissions.metadata IS 'Additional assignment metadata';
