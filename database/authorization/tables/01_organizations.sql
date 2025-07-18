-- ============================================================================
-- File: 01_organizations.sql
-- Description: Organizations table with hierarchical structure using LTREE
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authorization.organizations CASCADE;

CREATE TABLE authorization.organizations (
	id					UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	name				VARCHAR(255) NOT NULL,
	display_name		VARCHAR(255),
	description			TEXT,
	parent_id			UUID REFERENCES authorization.organizations(id),
	path				LTREE NOT NULL,
	metadata			JSONB DEFAULT '{}',
	settings			JSONB DEFAULT '{}',
	is_active			BOOLEAN DEFAULT true,
	created_at			TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	updated_at			TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	created_by			UUID,
	updated_by			UUID,
	
	-- Constraints
	CONSTRAINT organizations_path_unique		UNIQUE(path),
	CONSTRAINT organizations_name_length		CHECK(length(name) >= 2),
	CONSTRAINT organizations_no_self_parent		CHECK(id != parent_id)
);

-- Indexes for performance
CREATE INDEX idx_orgs_path_gist				ON authorization.organizations USING GIST(path);
CREATE INDEX idx_orgs_path_btree			ON authorization.organizations USING BTREE(path);
CREATE INDEX idx_orgs_parent_id				ON authorization.organizations(parent_id);
CREATE INDEX idx_orgs_active				ON authorization.organizations(is_active) WHERE is_active = true;
CREATE INDEX idx_orgs_created_at			ON authorization.organizations(created_at);
CREATE INDEX idx_orgs_metadata				ON authorization.organizations USING GIN(metadata);
CREATE INDEX idx_orgs_name					ON authorization.organizations(name);
CREATE INDEX idx_orgs_name_lower			ON authorization.organizations(lower(name));

-- Comments
COMMENT ON TABLE authorization.organizations IS 'Hierarchical organization structure with LTREE paths for efficient queries';
COMMENT ON COLUMN authorization.organizations.id IS 'Unique identifier for the organization';
COMMENT ON COLUMN authorization.organizations.name IS 'Internal name used in path generation (no spaces or special characters)';
COMMENT ON COLUMN authorization.organizations.display_name IS 'Human-readable name for UI display';
COMMENT ON COLUMN authorization.organizations.path IS 'LTREE path representing organization hierarchy (e.g., company.division.department)';
COMMENT ON COLUMN authorization.organizations.parent_id IS 'Reference to parent organization, NULL for root organizations';
COMMENT ON COLUMN authorization.organizations.metadata IS 'Additional flexible data for organization-specific attributes';
COMMENT ON COLUMN authorization.organizations.settings IS 'Organization-specific configuration settings';
COMMENT ON COLUMN authorization.organizations.is_active IS 'Soft delete flag - inactive organizations are hidden but not deleted';

-- Trigger to update updated_at timestamp
CREATE OR REPLACE FUNCTION authorization.update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
	NEW.updated_at = CURRENT_TIMESTAMP;
	RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER update_organizations_updated_at 
	BEFORE UPDATE ON authorization.organizations
	FOR EACH ROW 
	EXECUTE FUNCTION authorization.update_updated_at_column();