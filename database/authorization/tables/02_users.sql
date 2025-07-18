-- ============================================================================
-- File: 02_users.sql
-- Description: Users table for storing user account information
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authz.users CASCADE;

CREATE TABLE authz.users (
	id						UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
	external_id				VARCHAR(255) UNIQUE,  -- From identity provider
	email					VARCHAR(255) NOT NULL,
	username				VARCHAR(100),
	first_name				VARCHAR(100),
	last_name				VARCHAR(100),
	display_name			VARCHAR(200),
	avatar_url				TEXT,
	phone					VARCHAR(50),
	locale					VARCHAR(10) DEFAULT 'en-US',
	timezone				VARCHAR(50) DEFAULT 'UTC',
	metadata				JSONB DEFAULT '{}',
	preferences				JSONB DEFAULT '{}',
	is_active				BOOLEAN DEFAULT true,
	is_system_user			BOOLEAN DEFAULT false,
	email_verified			BOOLEAN DEFAULT false,
	last_login_at			TIMESTAMP WITH TIME ZONE,
	password_changed_at		TIMESTAMP WITH TIME ZONE,
	created_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	updated_at				TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
	
	-- Constraints
	CONSTRAINT users_email_unique			UNIQUE(email),
	CONSTRAINT users_email_format			CHECK(email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'),
	CONSTRAINT users_username_format		CHECK(username IS NULL OR username ~* '^[a-zA-Z0-9_.-]{3,50}$')
);

-- Indexes for performance
CREATE INDEX idx_users_email				ON authz.users(email);
CREATE INDEX idx_users_email_lower			ON authz.users(lower(email));
CREATE INDEX idx_users_external_id			ON authz.users(external_id);
CREATE INDEX idx_users_username				ON authz.users(username);
CREATE INDEX idx_users_username_lower		ON authz.users(lower(username));
CREATE INDEX idx_users_active				ON authz.users(is_active) WHERE is_active = true;
CREATE INDEX idx_users_last_login			ON authz.users(last_login_at);
CREATE INDEX idx_users_metadata				ON authz.users USING GIN(metadata);
CREATE INDEX idx_users_system				ON authz.users(is_system_user) WHERE is_system_user = true;
CREATE INDEX idx_users_display_name			ON authz.users(display_name);

-- Comments
COMMENT ON TABLE  authz.users 				IS 'User accounts for the RBAC system';
COMMENT ON COLUMN authz.users.id 			IS 'Internal unique identifier';
COMMENT ON COLUMN authz.users.external_id 	IS 'Identifier from external identity provider (SSO, OAuth, etc.)';
COMMENT ON COLUMN authz.users.email 		IS 'Primary email address - must be unique';
COMMENT ON COLUMN authz.users.username 		IS 'Optional username for login - must be unique if provided';
COMMENT ON COLUMN authz.users.display_name 	IS 'Full name for display purposes';
COMMENT ON COLUMN authz.users.metadata 		IS 'Flexible JSON storage for additional user attributes';
COMMENT ON COLUMN authz.users.preferences 	IS 'User-specific settings and preferences';
COMMENT ON COLUMN authz.users.is_active 	IS 'Account active status - inactive users cannot authenticate';
COMMENT ON COLUMN authz.users.is_system_user IS 'Flag for system/service accounts';
COMMENT ON COLUMN authz.users.email_verified IS 'Email verification status';
COMMENT ON COLUMN authz.users.last_login_at IS 'Timestamp of most recent successful login';

-- Trigger to update updated_at timestamp
CREATE TRIGGER update_users_updated_at 
	BEFORE UPDATE ON authz.users
	FOR EACH ROW 
	EXECUTE FUNCTION authz.update_updated_at_column();