-- ============================================================================
-- File: 05_permissions.sql
-- Description: Permissions table for defining available system permissions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authorization.permissions CASCADE;

CREATE TABLE authorization.permissions (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	permission				VARCHAR(255) NOT NULL,
	display_name			VARCHAR(255),
	description				TEXT,
	category				VARCHAR(50) NOT NULL,
	resource				VARCHAR(100),
	action					VARCHAR(100),
	scope					VARCHAR(50) DEFAULT 'organization',
	is_system_permission	BOOLEAN DEFAULT false,
	is_dangerous			BOOLEAN DEFAULT false,
	metadata				JSONB DEFAULT '{}',
	created_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	
	-- Constraints
	CONSTRAINT permissions_unique			UNIQUE(permission),
	CONSTRAINT permissions_format			CHECK(permission ~* '^[a-z_]+:[a-z_*]+$'),
	CONSTRAINT permissions_category_valid	CHECK(category IN ('table', 'collection', 'system', 'api', 'feature', 'ui')),
	CONSTRAINT permissions_scope_valid		CHECK(scope IN ('organization', 'system', 'global'))
);

-- Indexes for performance
CREATE INDEX idx_permissions_permission			ON authorization.permissions(permission);
CREATE INDEX idx_permissions_category			ON authorization.permissions(category);
CREATE INDEX idx_permissions_resource			ON authorization.permissions(resource);
CREATE INDEX idx_permissions_action				ON authorization.permissions(action);
CREATE INDEX idx_permissions_scope				ON authorization.permissions(scope);
CREATE INDEX idx_permissions_system				ON authorization.permissions(is_system_permission);
CREATE INDEX idx_permissions_dangerous			ON authorization.permissions(is_dangerous);
CREATE INDEX idx_permissions_metadata			ON authorization.permissions USING GIN(metadata);
CREATE INDEX idx_permissions_resource_action	ON authorization.permissions(resource, action);

-- Comments
COMMENT ON TABLE authorization.permissions IS 'Master list of all available permissions in the system';
COMMENT ON COLUMN authorization.permissions.id IS 'Unique permission identifier';
COMMENT ON COLUMN authorization.permissions.permission IS 'Permission string in format resource:action (e.g., users:read, posts:write)';
COMMENT ON COLUMN authorization.permissions.display_name IS 'Human-readable permission name';
COMMENT ON COLUMN authorization.permissions.description IS 'Detailed description of what this permission allows';
COMMENT ON COLUMN authorization.permissions.category IS 'Permission category: table (DB), collection, system, api, feature, ui';
COMMENT ON COLUMN authorization.permissions.resource IS 'Resource name extracted from permission string';
COMMENT ON COLUMN authorization.permissions.action IS 'Action name extracted from permission string';
COMMENT ON COLUMN authorization.permissions.scope IS 'Permission scope: organization (org-level), system (cross-org), global';
COMMENT ON COLUMN authorization.permissions.is_system_permission IS 'Whether this is a protected system permission';
COMMENT ON COLUMN authorization.permissions.is_dangerous IS 'Whether this permission requires additional confirmation/audit';
COMMENT ON COLUMN authorization.permissions.metadata IS 'Additional permission configuration (e.g., UI hints, risk level)';

-- Function to extract resource and action from permission string
CREATE OR REPLACE FUNCTION authorization.extract_permission_parts()
RETURNS TRIGGER AS $$
BEGIN
	-- Extract resource and action from permission string
	NEW.resource := split_part(NEW.permission, ':', 1);
	NEW.action := split_part(NEW.permission, ':', 2);
	RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger to auto-populate resource and action columns
CREATE TRIGGER extract_permission_parts_trigger
	BEFORE INSERT OR UPDATE ON authorization.permissions
	FOR EACH ROW
	EXECUTE FUNCTION authorization.extract_permission_parts();