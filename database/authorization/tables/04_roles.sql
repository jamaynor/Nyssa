-- ============================================================================
-- File: 04_roles.sql
-- Description: Roles table for organization-specific role definitions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authz.roles CASCADE;

CREATE TABLE authz.roles (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	organization_id			UUID NOT NULL REFERENCES authz.organizations(id) ON DELETE CASCADE,
	name					VARCHAR(100) NOT NULL,
	display_name			VARCHAR(255),
	description				TEXT,
	role_type				VARCHAR(50) DEFAULT 'custom',
	is_inheritable			BOOLEAN DEFAULT true,
	is_system_role			BOOLEAN DEFAULT false,
	is_assignable			BOOLEAN DEFAULT true,
	priority				INTEGER DEFAULT 0,
	color					VARCHAR(7),
	metadata				JSONB DEFAULT '{}',
	settings				JSONB DEFAULT '{}',
	is_active				BOOLEAN DEFAULT true,
	created_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	updated_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	created_by				UUID REFERENCES authz.users(id),
	updated_by				UUID REFERENCES authz.users(id),
	
	-- Constraints
	CONSTRAINT roles_org_name_unique		UNIQUE(organization_id, name),
	CONSTRAINT roles_name_length			CHECK(length(name) >= 2),
	CONSTRAINT roles_type_valid				CHECK(role_type IN ('system', 'custom', 'template')),
	CONSTRAINT roles_color_format			CHECK(color IS NULL OR color ~* '^#[0-9A-Fa-f]{6}$')
);

-- Indexes for performance
CREATE INDEX idx_roles_org_id				ON authz.roles(organization_id);
CREATE INDEX idx_roles_name					ON authz.roles(name);
CREATE INDEX idx_roles_active				ON authz.roles(organization_id, is_active) WHERE is_active = true;
CREATE INDEX idx_roles_inheritable			ON authz.roles(is_inheritable) WHERE is_inheritable = true;
CREATE INDEX idx_roles_system				ON authz.roles(is_system_role) WHERE is_system_role = true;
CREATE INDEX idx_roles_assignable			ON authz.roles(is_assignable) WHERE is_assignable = true;
CREATE INDEX idx_roles_priority				ON authz.roles(priority);
CREATE INDEX idx_roles_type					ON authz.roles(role_type);
CREATE INDEX idx_roles_metadata				ON authz.roles USING GIN(metadata);
CREATE INDEX idx_roles_created_at			ON authz.roles(created_at);

-- Comments
COMMENT ON TABLE authz.roles IS 'Organization-specific role definitions';
COMMENT ON COLUMN authz.roles.id IS 'Unique role identifier';
COMMENT ON COLUMN authz.roles.organization_id IS 'Organization that owns this role';
COMMENT ON COLUMN authz.roles.name IS 'Internal role name (e.g., admin, manager, viewer)';
COMMENT ON COLUMN authz.roles.display_name IS 'Human-readable role name for UI';
COMMENT ON COLUMN authz.roles.description IS 'Role purpose and permissions description';
COMMENT ON COLUMN authz.roles.role_type IS 'Role classification: system (built-in), custom (org-created), template (copyable)';
COMMENT ON COLUMN authz.roles.is_inheritable IS 'Whether this role permissions cascade to child organizations';
COMMENT ON COLUMN authz.roles.is_system_role IS 'Whether this is a protected system role';
COMMENT ON COLUMN authz.roles.is_assignable IS 'Whether users can be assigned this role';
COMMENT ON COLUMN authz.roles.priority IS 'Role precedence for conflict resolution (higher wins)';
COMMENT ON COLUMN authz.roles.color IS 'UI color for role badges (hex format)';
COMMENT ON COLUMN authz.roles.metadata IS 'Additional role configuration data';
COMMENT ON COLUMN authz.roles.settings IS 'Role-specific settings and configurations';

-- Trigger to update updated_at timestamp
CREATE TRIGGER update_roles_updated_at 
	BEFORE UPDATE ON authz.roles
	FOR EACH ROW 
	EXECUTE FUNCTION authz.update_updated_at_column();