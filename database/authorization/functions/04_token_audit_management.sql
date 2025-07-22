-- ============================================================================
-- File: 04_token_audit_management.sql
-- Description: Functions for token management and audit logging
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

-- ============================================================================
-- Function: blacklist_token
-- Description: Adds a token to the blacklist for immediate revocation
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.blacklist_token(
	p_token_jti				VARCHAR(255),
	p_user_id				UUID,
	p_organization_id		UUID DEFAULT NULL,
	p_revoked_by			UUID DEFAULT NULL,
	p_reason				VARCHAR DEFAULT NULL,
	p_expires_at			TIMESTAMP WITH TIME ZONE DEFAULT NULL
)
RETURNS BOOLEAN AS $$
BEGIN
	-- Insert into blacklist
	INSERT INTO authz.token_blacklist (
		token_jti, user_id, organization_id, revoked_by, reason, expires_at
	) VALUES (
		p_token_jti, p_user_id, p_organization_id, p_revoked_by, p_reason, 
		COALESCE(p_expires_at, CURRENT_TIMESTAMP + INTERVAL '24 hours')
	)
	ON CONFLICT (token_jti) DO UPDATE SET
		revoked_at = CURRENT_TIMESTAMP,
		reason = EXCLUDED.reason;
	
	-- Log the revocation
	PERFORM authz.log_audit_event(
		'TOKEN_REVOKED',
		'SECURITY',
		p_revoked_by,
		p_organization_id,
		'TOKEN',
		uuid_generate_v4(),  -- Generate UUID for token tracking
		'REVOKE',
		'success',
		jsonb_build_object(
			'token_jti', p_token_jti,
			'user_id', p_user_id,
			'organization_id', p_organization_id,
			'reason', p_reason
		)
	);
	
	RETURN true;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: is_token_blacklisted
-- Description: Checks if a token is blacklisted
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.is_token_blacklisted(
	p_token_jti				VARCHAR(255)
)
RETURNS BOOLEAN AS $$
BEGIN
	RETURN EXISTS(
		SELECT 1 FROM authz.token_blacklist
		WHERE token_jti = p_token_jti
			AND expires_at > CURRENT_TIMESTAMP
	);
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: emergency_revoke_user_tokens
-- Description: Emergency revocation of all tokens for a user
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.emergency_revoke_user_tokens(
	p_user_id				UUID,
	p_revoked_by			UUID,
	p_reason				VARCHAR DEFAULT 'Emergency revocation'
)
RETURNS INTEGER AS $$
DECLARE
	revoked_count			INTEGER := 0;
BEGIN
	-- Insert a special blacklist entry that blocks all tokens for this user
	INSERT INTO authz.token_blacklist (
		token_jti, user_id, revoked_by, reason, expires_at, metadata
	) VALUES (
		'EMERGENCY_' || p_user_id::text, 
		p_user_id, 
		p_revoked_by, 
		p_reason,
		CURRENT_TIMESTAMP + INTERVAL '24 hours',
		jsonb_build_object('emergency_revocation', true, 'all_tokens', true)
	)
	ON CONFLICT (token_jti) DO UPDATE SET
		revoked_at = CURRENT_TIMESTAMP,
		reason = EXCLUDED.reason,
		expires_at = EXCLUDED.expires_at;
	
	-- Log emergency revocation
	PERFORM authz.log_audit_event(
		'EMERGENCY_TOKEN_REVOCATION',
		'SECURITY',
		p_revoked_by,
		NULL,
		'USER',
		p_user_id,
		'EMERGENCY_REVOKE',
		'success',
		jsonb_build_object(
			'user_id', p_user_id,
			'reason', p_reason,
			'severity', 'HIGH'
		)
	);
	
	RETURN 1; -- Indicates emergency revocation was set
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: log_audit_event
-- Description: Centralized audit logging function
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.log_audit_event(
	p_event_type			VARCHAR(100),
	p_event_category		VARCHAR(50),
	p_user_id				UUID DEFAULT NULL,
	p_organization_id		UUID DEFAULT NULL,
	p_resource_type			VARCHAR(100) DEFAULT NULL,
	p_resource_id			UUID DEFAULT NULL,
	p_action				VARCHAR(100) DEFAULT NULL,
	p_result				VARCHAR(50) DEFAULT 'success',
	p_details				JSONB DEFAULT '{}',
	p_ip_address			INET DEFAULT NULL,
	p_user_agent			TEXT DEFAULT NULL,
	p_session_id			UUID DEFAULT NULL,
	p_request_id			UUID DEFAULT NULL
)
RETURNS UUID AS $$
DECLARE
	event_id				UUID;
BEGIN
	event_id := uuid_generate_v4();
	
	INSERT INTO authz.audit_events (
		id, event_type, event_category, user_id, organization_id,
		resource_type, resource_id, action, result, details,
		ip_address, user_agent, session_id, request_id, occurred_at
	) VALUES (
		event_id, p_event_type, p_event_category, p_user_id, p_organization_id,
		p_resource_type, p_resource_id, p_action, p_result, p_details,
		p_ip_address, p_user_agent, p_session_id, p_request_id, CURRENT_TIMESTAMP
	);
	
	RETURN event_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: invalidate_user_permission_cache
-- Description: Marks user permissions for cache invalidation
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.invalidate_user_permission_cache(
	p_user_id				UUID,
	p_organization_id		UUID DEFAULT NULL
)
RETURNS VOID AS $$
BEGIN
	-- This function is called by the application layer to invalidate Redis cache
	-- Log cache invalidation for debugging
	PERFORM authz.log_audit_event(
		'CACHE_INVALIDATED',
		'SYSTEM',
		p_user_id,
		p_organization_id,
		'CACHE',
		NULL,
		'INVALIDATE',
		'success',
		jsonb_build_object(
			'cache_type', 'user_permissions',
			'user_id', p_user_id,
			'organization_id', p_organization_id
		)
	);
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: cleanup_expired_tokens
-- Description: Removes expired tokens from blacklist
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.cleanup_expired_tokens()
RETURNS TABLE(
	deleted_count			INTEGER
) AS $$
DECLARE
	cleanup_count			INTEGER;
BEGIN
	DELETE FROM authz.token_blacklist
	WHERE expires_at < CURRENT_TIMESTAMP;
	
	GET DIAGNOSTICS cleanup_count = ROW_COUNT;
	
	-- Log cleanup if any tokens were removed
	IF cleanup_count > 0 THEN
		PERFORM authz.log_audit_event(
			'TOKEN_CLEANUP',
			'SYSTEM',
			NULL,
			NULL,
			'TOKEN_BLACKLIST',
			NULL,
			'CLEANUP',
			'success',
			jsonb_build_object(
				'deleted_count', cleanup_count,
				'cleanup_type', 'expired_tokens'
			)
		);
	END IF;
	
	RETURN QUERY SELECT cleanup_count;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function: get_audit_events
-- Description: Retrieves audit events with filtering options
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_audit_events(
	p_start_date			TIMESTAMP WITH TIME ZONE DEFAULT NULL,
	p_end_date				TIMESTAMP WITH TIME ZONE DEFAULT NULL,
	p_user_id				UUID DEFAULT NULL,
	p_organization_id		UUID DEFAULT NULL,
	p_event_type			VARCHAR DEFAULT NULL,
	p_event_category		VARCHAR DEFAULT NULL,
	p_result				VARCHAR DEFAULT NULL,
	p_limit					INTEGER DEFAULT 100,
	p_offset				INTEGER DEFAULT 0
)
RETURNS TABLE(
	id						UUID,
	event_type				VARCHAR,
	event_category			VARCHAR,
	user_id					UUID,
	organization_id			UUID,
	resource_type			VARCHAR,
	resource_id				UUID,
	action					VARCHAR,
	result					VARCHAR,
	ip_address				INET,
	details					JSONB,
	occurred_at				TIMESTAMP WITH TIME ZONE
) AS $$
BEGIN
	RETURN QUERY
	SELECT 
		ae.id,
		ae.event_type,
		ae.event_category,
		ae.user_id,
		ae.organization_id,
		ae.resource_type,
		ae.resource_id,
		ae.action,
		ae.result,
		ae.ip_address,
		ae.details,
		ae.occurred_at
	FROM authz.audit_events ae
	WHERE (p_start_date IS NULL OR ae.occurred_at >= p_start_date)
		AND (p_end_date IS NULL OR ae.occurred_at <= p_end_date)
		AND (p_user_id IS NULL OR ae.user_id = p_user_id)
		AND (p_organization_id IS NULL OR ae.organization_id = p_organization_id)
		AND (p_event_type IS NULL OR ae.event_type = p_event_type)
		AND (p_event_category IS NULL OR ae.event_category = p_event_category)
		AND (p_result IS NULL OR ae.result = p_result)
	ORDER BY ae.occurred_at DESC
	LIMIT p_limit
	OFFSET p_offset;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: get_security_events_summary
-- Description: Returns a summary of security events for monitoring
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.get_security_events_summary(
	p_time_window			INTERVAL DEFAULT INTERVAL '1 hour'
)
RETURNS TABLE(
	event_type				VARCHAR,
	event_count				BIGINT,
	unique_users			BIGINT,
	unique_ips				BIGINT,
	failure_count			BIGINT,
	first_occurrence		TIMESTAMP WITH TIME ZONE,
	last_occurrence			TIMESTAMP WITH TIME ZONE
) AS $$
BEGIN
	RETURN QUERY
	SELECT 
		ae.event_type,
		COUNT(*) as event_count,
		COUNT(DISTINCT ae.user_id) as unique_users,
		COUNT(DISTINCT ae.ip_address) as unique_ips,
		COUNT(*) FILTER (WHERE ae.result = 'failure') as failure_count,
		MIN(ae.occurred_at) as first_occurrence,
		MAX(ae.occurred_at) as last_occurrence
	FROM authz.audit_events ae
	WHERE ae.event_category IN ('SECURITY', 'AUTHENTICATION', 'AUTHORIZATION')
		AND ae.occurred_at >= CURRENT_TIMESTAMP - p_time_window
	GROUP BY ae.event_type
	ORDER BY event_count DESC;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================================
-- Function: detect_suspicious_activity
-- Description: Detects potential security threats based on audit patterns
-- ============================================================================
CREATE OR REPLACE FUNCTION authz.detect_suspicious_activity(
	p_threshold_minutes		INTEGER DEFAULT 5,
	p_failure_threshold		INTEGER DEFAULT 5
)
RETURNS TABLE(
	threat_type				VARCHAR,
	user_id					UUID,
	ip_address				INET,
	event_count				BIGINT,
	details					JSONB
) AS $$
BEGIN
	-- Detect brute force attempts
	RETURN QUERY
	SELECT 
		'BRUTE_FORCE_ATTEMPT'::VARCHAR as threat_type,
		ae.user_id,
		ae.ip_address,
		COUNT(*) as event_count,
		jsonb_build_object(
			'failed_attempts', COUNT(*),
			'time_window_minutes', p_threshold_minutes,
			'first_attempt', MIN(ae.occurred_at),
			'last_attempt', MAX(ae.occurred_at)
		) as details
	FROM authz.audit_events ae
	WHERE ae.event_type IN ('AUTHENTICATION_FAILED', 'PERMISSION_DENIED')
		AND ae.result = 'failure'
		AND ae.occurred_at >= CURRENT_TIMESTAMP - (p_threshold_minutes || ' minutes')::INTERVAL
	GROUP BY ae.user_id, ae.ip_address
	HAVING COUNT(*) >= p_failure_threshold;
	
	-- Detect unusual access patterns (multiple organizations in short time)
	RETURN QUERY
	SELECT 
		'UNUSUAL_ACCESS_PATTERN'::VARCHAR as threat_type,
		ae.user_id,
		ae.ip_address,
		COUNT(DISTINCT ae.organization_id) as event_count,
		jsonb_build_object(
			'organizations_accessed', array_agg(DISTINCT ae.organization_id),
			'time_window_minutes', p_threshold_minutes
		) as details
	FROM authz.audit_events ae
	WHERE ae.event_type = 'PERMISSION_CHECK'
		AND ae.occurred_at >= CURRENT_TIMESTAMP - (p_threshold_minutes || ' minutes')::INTERVAL
		AND ae.user_id IS NOT NULL
	GROUP BY ae.user_id, ae.ip_address
	HAVING COUNT(DISTINCT ae.organization_id) > 3;
END;
$$ LANGUAGE plpgsql STABLE;

-- Comments
COMMENT ON FUNCTION authz.blacklist_token IS 'Adds a JWT token to the blacklist for immediate revocation';
COMMENT ON FUNCTION authz.is_token_blacklisted IS 'Checks if a token is currently blacklisted';
COMMENT ON FUNCTION authz.emergency_revoke_user_tokens IS 'Emergency revocation of all tokens for a specific user';
COMMENT ON FUNCTION authz.log_audit_event IS 'Central audit logging function for all system events';
COMMENT ON FUNCTION authz.invalidate_user_permission_cache IS 'Marks user permissions for cache invalidation';
COMMENT ON FUNCTION authz.cleanup_expired_tokens IS 'Removes expired tokens from the blacklist';
COMMENT ON FUNCTION authz.get_audit_events IS 'Retrieves audit events with flexible filtering options';
COMMENT ON FUNCTION authz.get_security_events_summary IS 'Returns a summary of security events for monitoring dashboards';
COMMENT ON FUNCTION authz.detect_suspicious_activity IS 'Detects potential security threats based on audit event patterns';