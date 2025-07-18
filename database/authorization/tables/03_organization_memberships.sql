-- ============================================================================
-- File: 03_organization_memberships.sql
-- Description: Organization memberships table for user-organization relationships
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authorization.organization_memberships CASCADE;

CREATE TABLE authorization.organization_memberships (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	user_id					UUID NOT NULL REFERENCES authorization.users(id) ON DELETE CASCADE,
	organization_id			UUID NOT NULL REFERENCES authorization.organizations(id) ON DELETE CASCADE,
	status					VARCHAR(50) DEFAULT 'active',
	membership_type			VARCHAR(50) DEFAULT 'member',
	joined_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	invited_by				UUID REFERENCES authorization.users(id),
	invitation_token		VARCHAR(255),
	invitation_expires_at	TIMESTAMP WITH TIME ZONE,
	left_at					TIMESTAMP WITH TIME ZONE,
	metadata				JSONB DEFAULT '{}',
	
	-- Constraints
	CONSTRAINT org_memberships_unique			UNIQUE(user_id, organization_id),
	CONSTRAINT org_memberships_status_valid		CHECK(status IN ('active', 'inactive', 'pending', 'suspended')),
	CONSTRAINT org_memberships_type_valid		CHECK(membership_type IN ('member', 'admin', 'owner', 'guest'))
);

-- Indexes for performance
CREATE INDEX idx_org_memberships_user_id		ON authorization.organization_memberships(user_id);
CREATE INDEX idx_org_memberships_org_id			ON authorization.organization_memberships(organization_id);
CREATE INDEX idx_org_memberships_status			ON authorization.organization_memberships(status);
CREATE INDEX idx_org_memberships_type			ON authorization.organization_memberships(membership_type);
CREATE INDEX idx_org_memberships_active			ON authorization.organization_memberships(user_id, organization_id) 
	WHERE status = 'active';
CREATE INDEX idx_org_memberships_joined			ON authorization.organization_memberships(joined_at);
CREATE INDEX idx_org_memberships_token			ON authorization.organization_memberships(invitation_token) 
	WHERE invitation_token IS NOT NULL;
CREATE INDEX idx_org_memberships_pending		ON authorization.organization_memberships(invitation_expires_at) 
	WHERE status = 'pending';

-- Comments
COMMENT ON TABLE authorization.organization_memberships IS 'Tracks user membership in organizations with status and type';
COMMENT ON COLUMN authorization.organization_memberships.id IS 'Unique membership record identifier';
COMMENT ON COLUMN authorization.organization_memberships.user_id IS 'Reference to the user who is a member';
COMMENT ON COLUMN authorization.organization_memberships.organization_id IS 'Reference to the organization';
COMMENT ON COLUMN authorization.organization_memberships.status IS 'Membership status: active, inactive, pending (invitation), suspended';
COMMENT ON COLUMN authorization.organization_memberships.membership_type IS 'Type of membership: member (regular), admin (org admin), owner, guest';
COMMENT ON COLUMN authorization.organization_memberships.joined_at IS 'When the user joined the organization';
COMMENT ON COLUMN authorization.organization_memberships.invited_by IS 'User who sent the invitation';
COMMENT ON COLUMN authorization.organization_memberships.invitation_token IS 'Unique token for invitation acceptance';
COMMENT ON COLUMN authorization.organization_memberships.invitation_expires_at IS 'Expiration time for pending invitations';
COMMENT ON COLUMN authorization.organization_memberships.left_at IS 'When user left the organization (for inactive status)';
COMMENT ON COLUMN authorization.organization_memberships.metadata IS 'Additional membership data (e.g., invitation message, notes)';