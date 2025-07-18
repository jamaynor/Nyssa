-- ============================================================================
-- File: 08_token_blacklist.sql
-- Description: Token blacklist table for tracking revoked JWT tokens
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS auth.token_blacklist CASCADE;

CREATE TABLE authz.token_blacklist (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	token_jti				VARCHAR(255) NOT NULL UNIQUE,
	user_id					UUID REFERENCES authz.users(id),
	organization_id			UUID REFERENCES authz.organizations(id),
	revoked_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	revoked_by				UUID REFERENCES authz.users(id),
	reason					VARCHAR(255),
	expires_at				TIMESTAMP WITH TIME ZONE NOT NULL,
	metadata				JSONB DEFAULT '{}',
	
	-- Constraints
	CONSTRAINT token_blacklist_jti_unique		UNIQUE(token_jti)
);

-- Indexes for performance
CREATE INDEX idx_token_blacklist_jti			ON authz.token_blacklist(token_jti);
CREATE INDEX idx_token_blacklist_user_id		ON authz.token_blacklist(user_id);
CREATE INDEX idx_token_blacklist_org_id			ON authz.token_blacklist(organization_id);
CREATE INDEX idx_token_blacklist_expires_at		ON authz.token_blacklist(expires_at);
CREATE INDEX idx_token_blacklist_revoked_at		ON authz.token_blacklist(revoked_at);
CREATE INDEX idx_token_blacklist_cleanup		ON authz.token_blacklist(expires_at);

-- Comments
COMMENT ON TABLE authz.token_blacklist IS 'Tracks revoked JWT tokens for immediate access revocation';
COMMENT ON COLUMN authz.token_blacklist.id IS 'Unique blacklist entry identifier';
COMMENT ON COLUMN authz.token_blacklist.token_jti IS 'JWT ID claim - unique identifier for the token';
COMMENT ON COLUMN authz.token_blacklist.user_id IS 'User whose token was revoked';
COMMENT ON COLUMN authz.token_blacklist.organization_id IS 'Organization context if applicable';
COMMENT ON COLUMN authz.token_blacklist.revoked_at IS 'When the token was revoked';
COMMENT ON COLUMN authz.token_blacklist.revoked_by IS 'User who revoked the token';
COMMENT ON COLUMN authz.token_blacklist.reason IS 'Reason for revocation (e.g., logout, security, password change)';
COMMENT ON COLUMN authz.token_blacklist.expires_at IS 'Token expiration time - used for cleanup';
COMMENT ON COLUMN authz.token_blacklist.metadata IS 'Additional revocation data (e.g., IP address, device info)';