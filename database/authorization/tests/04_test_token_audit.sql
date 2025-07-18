-- ============================================================================
-- File: 04_test_token_audit.sql
-- Description: Test suite for token management and audit functions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Enable detailed error messages
\set VERBOSITY verbose

-- ============================================================================
-- Test Helper Functions
-- ============================================================================
CREATE OR REPLACE FUNCTION test.setup_token_audit_test_data()
RETURNS TABLE(
	test_user1_id		UUID,
	test_user2_id		UUID,
	org_id				UUID
) AS $$
DECLARE
	v_test_user1_id		UUID;
	v_test_user2_id		UUID;
	v_org_id			UUID;
BEGIN
	-- Create test users
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('tokenuser1@test.com', 'Token', 'User1', '{"test_context": true}')
	RETURNING id INTO v_test_user1_id;
	
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('tokenuser2@test.com', 'Token', 'User2', '{"test_context": true}')
	RETURNING id INTO v_test_user2_id;
	
	-- Create test organization
	INSERT INTO authorization.organizations (name, display_name, path, metadata)
	VALUES ('TokenTestOrg', 'Token Test Organization', 'tokentestorg', '{"test_context": true}')
	RETURNING id INTO v_org_id;
	
	-- Add users as members
	INSERT INTO authorization.organization_memberships (user_id, organization_id, metadata)
	VALUES 
		(v_test_user1_id, v_org_id, '{"test_context": true}'),
		(v_test_user2_id, v_org_id, '{"test_context": true}');
	
	RETURN QUERY SELECT v_test_user1_id, v_test_user2_id, v_org_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Test: Token Blacklist Management
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	token_jti1			VARCHAR := 'test_token_' || uuid_generate_v4()::text;
	token_jti2			VARCHAR := 'test_token_' || uuid_generate_v4()::text;
	blacklist_result	BOOLEAN;
	is_blacklisted		BOOLEAN;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Token Blacklist Management - Starting';
	
	-- Setup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.token_blacklist WHERE metadata->>'test_context' = 'true' OR reason = 'Test token';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Test 1: Blacklist a token
	BEGIN
		blacklist_result := authorization.blacklist_token(
			token_jti1,
			test_data.test_user1_id,
			test_data.org_id,
			test_data.test_user2_id,
			'Test token',
			CURRENT_TIMESTAMP + INTERVAL '1 hour'
		);
		
		ASSERT blacklist_result = true, 'Blacklist should return true';
		
		-- Verify token is blacklisted
		is_blacklisted := authorization.is_token_blacklisted(token_jti1);
		ASSERT is_blacklisted = true, 'Token should be blacklisted';
		
		RAISE NOTICE 'TEST PASSED: Token blacklisted successfully';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Token blacklist - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Check non-blacklisted token
	BEGIN
		is_blacklisted := authorization.is_token_blacklisted('non_existent_token');
		
		ASSERT is_blacklisted = false, 'Non-existent token should not be blacklisted';
		
		RAISE NOTICE 'TEST PASSED: Non-blacklisted token check';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Non-blacklisted check - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Re-blacklist same token (should update)
	BEGIN
		blacklist_result := authorization.blacklist_token(
			token_jti1,
			test_data.test_user1_id,
			test_data.org_id,
			test_data.test_user2_id,
			'Updated reason',
			CURRENT_TIMESTAMP + INTERVAL '2 hours'
		);
		
		ASSERT blacklist_result = true, 'Re-blacklist should succeed';
		
		-- Verify reason was updated
		ASSERT EXISTS(
			SELECT 1 FROM authorization.token_blacklist
			WHERE token_jti = token_jti1
				AND reason = 'Updated reason'
		), 'Blacklist reason should be updated';
		
		RAISE NOTICE 'TEST PASSED: Token re-blacklist updates';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Re-blacklist - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Expired token not blacklisted
	BEGIN
		-- Add expired token
		INSERT INTO authorization.token_blacklist (
			token_jti, user_id, expires_at, reason
		) VALUES (
			token_jti2, test_data.test_user1_id, 
			CURRENT_TIMESTAMP - INTERVAL '1 hour', 'Expired test'
		);
		
		is_blacklisted := authorization.is_token_blacklisted(token_jti2);
		
		ASSERT is_blacklisted = false, 'Expired token should not be considered blacklisted';
		
		RAISE NOTICE 'TEST PASSED: Expired token not blacklisted';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Expired token check - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.token_blacklist WHERE metadata->>'test_context' = 'true' OR reason LIKE '%test%' OR reason LIKE '%Test%';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Token Blacklist Management';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Token Blacklist Management';
	END IF;
END $$;

-- ============================================================================
-- Test: Emergency Token Revocation
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	revoke_count		INTEGER;
	emergency_jti		VARCHAR;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Emergency Token Revocation - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Test 1: Emergency revoke all user tokens
	BEGIN
		revoke_count := authorization.emergency_revoke_user_tokens(
			test_data.test_user1_id,
			test_data.test_user2_id,
			'Security incident test'
		);
		
		ASSERT revoke_count = 1, 'Should return 1 for emergency revocation';
		
		-- Verify emergency token entry exists
		emergency_jti := 'EMERGENCY_' || test_data.test_user1_id::text;
		ASSERT EXISTS(
			SELECT 1 FROM authorization.token_blacklist
			WHERE token_jti = emergency_jti
				AND user_id = test_data.test_user1_id
				AND reason = 'Security incident test'
				AND metadata->>'emergency_revocation' = 'true'
		), 'Emergency revocation entry should exist';
		
		-- Verify audit event logged
		ASSERT EXISTS(
			SELECT 1 FROM authorization.audit_events
			WHERE event_type = 'EMERGENCY_TOKEN_REVOCATION'
				AND resource_id = test_data.test_user1_id
				AND details->>'severity' = 'HIGH'
		), 'Emergency revocation should be audited';
		
		RAISE NOTICE 'TEST PASSED: Emergency token revocation';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Emergency revocation - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Update existing emergency revocation
	BEGIN
		revoke_count := authorization.emergency_revoke_user_tokens(
			test_data.test_user1_id,
			test_data.test_user2_id,
			'Updated emergency reason'
		);
		
		ASSERT revoke_count = 1, 'Should still return 1';
		
		-- Verify reason updated
		emergency_jti := 'EMERGENCY_' || test_data.test_user1_id::text;
		ASSERT EXISTS(
			SELECT 1 FROM authorization.token_blacklist
			WHERE token_jti = emergency_jti
				AND reason = 'Updated emergency reason'
		), 'Emergency revocation reason should be updated';
		
		RAISE NOTICE 'TEST PASSED: Emergency revocation update';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Emergency update - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true' OR event_type = 'EMERGENCY_TOKEN_REVOCATION';
	DELETE FROM authorization.token_blacklist WHERE token_jti LIKE 'EMERGENCY_%' OR reason LIKE '%test%';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Emergency Token Revocation';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Emergency Token Revocation';
	END IF;
END $$;

-- ============================================================================
-- Test: Audit Event Logging
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	event_id			UUID;
	event_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Audit Event Logging - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Test 1: Log basic audit event
	BEGIN
		event_id := authorization.log_audit_event(
			'TEST_EVENT',
			'TEST_CATEGORY',
			test_data.test_user1_id,
			test_data.org_id,
			'TEST_RESOURCE',
			uuid_generate_v4(),
			'TEST_ACTION',
			'success',
			'{"test_field": "test_value"}'::jsonb,
			'192.168.1.100'::inet,
			'Test User Agent',
			'test_session_123',
			'test_request_456'
		);
		
		ASSERT event_id IS NOT NULL, 'Event ID should be returned';
		
		-- Verify event was logged
		ASSERT EXISTS(
			SELECT 1 FROM authorization.audit_events
			WHERE id = event_id
				AND event_type = 'TEST_EVENT'
				AND user_id = test_data.test_user1_id
				AND details->>'test_field' = 'test_value'
		), 'Audit event should be logged with all fields';
		
		RAISE NOTICE 'TEST PASSED: Basic audit event logging';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Basic audit logging - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Log event with minimal fields
	BEGIN
		event_id := authorization.log_audit_event(
			'MINIMAL_EVENT',
			'TEST_CATEGORY',
			NULL,  -- No user
			NULL,  -- No org
			NULL,  -- No resource type
			NULL,  -- No resource ID
			'MINIMAL_ACTION',
			'success'
		);
		
		ASSERT event_id IS NOT NULL, 'Minimal event should log successfully';
		
		RAISE NOTICE 'TEST PASSED: Minimal audit event logging';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Minimal audit logging - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Retrieve audit events
	BEGIN
		-- Log a few more events
		PERFORM authorization.log_audit_event(
			'RETRIEVE_TEST_' || i::text,
			'TEST_CATEGORY',
			test_data.test_user1_id,
			test_data.org_id,
			NULL, NULL,
			'TEST_ACTION',
			CASE WHEN i % 2 = 0 THEN 'success' ELSE 'failure' END
		) FROM generate_series(1, 5) i;
		
		-- Test filtering by user
		SELECT COUNT(*) INTO event_count
		FROM authorization.get_audit_events(
			NULL,  -- No start date
			NULL,  -- No end date
			test_data.test_user1_id,  -- Filter by user
			NULL,  -- No org filter
			NULL,  -- No event type filter
			'TEST_CATEGORY',  -- Filter by category
			NULL,  -- No result filter
			100,   -- Limit
			0      -- Offset
		);
		
		ASSERT event_count >= 6, 'Should retrieve at least 6 events for user';
		
		-- Test filtering by result
		SELECT COUNT(*) INTO event_count
		FROM authorization.get_audit_events(
			CURRENT_TIMESTAMP - INTERVAL '1 hour',  -- Recent events
			NULL,
			test_data.test_user1_id,
			NULL,
			NULL,
			'TEST_CATEGORY',
			'failure',  -- Only failures
			100,
			0
		);
		
		ASSERT event_count >= 2, 'Should retrieve failure events';
		
		RAISE NOTICE 'TEST PASSED: Audit event retrieval with filters';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Event retrieval - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE event_category = 'TEST_CATEGORY';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Audit Event Logging';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Audit Event Logging';
	END IF;
END $$;

-- ============================================================================
-- Test: Security Event Monitoring
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	security_summary	RECORD;
	threat_record		RECORD;
	event_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Security Event Monitoring - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Create security events for testing
	BEGIN
		-- Simulate failed authentication attempts
		FOR i IN 1..6 LOOP
			PERFORM authorization.log_audit_event(
				'AUTHENTICATION_FAILED',
				'SECURITY',
				test_data.test_user1_id,
				NULL,
				'USER',
				test_data.test_user1_id,
				'LOGIN',
				'failure',
				'{"attempt": ' || i || '}'::jsonb,
				'192.168.1.100'::inet
			);
		END LOOP;
		
		-- Simulate permission checks across multiple orgs
		FOR i IN 1..5 LOOP
			PERFORM authorization.log_audit_event(
				'PERMISSION_CHECK',
				'AUTHORIZATION',
				test_data.test_user2_id,
				uuid_generate_v4(),  -- Different org each time
				'PERMISSION',
				NULL,
				'CHECK',
				'success',
				'{"org_hop": ' || i || '}'::jsonb,
				'10.0.0.50'::inet
			);
		END LOOP;
		
		-- Test 1: Security events summary
		SELECT * INTO security_summary
		FROM authorization.get_security_events_summary(INTERVAL '1 hour')
		WHERE event_type = 'AUTHENTICATION_FAILED';
		
		ASSERT security_summary.event_count = 6, 'Should have 6 failed auth events';
		ASSERT security_summary.failure_count = 6, 'All should be failures';
		ASSERT security_summary.unique_users = 1, 'Should be 1 unique user';
		ASSERT security_summary.unique_ips = 1, 'Should be 1 unique IP';
		
		RAISE NOTICE 'TEST PASSED: Security events summary';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Security summary - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Detect suspicious activity
	BEGIN
		-- Check brute force detection
		SELECT COUNT(*) INTO event_count
		FROM authorization.detect_suspicious_activity(10, 5)
		WHERE threat_type = 'BRUTE_FORCE_ATTEMPT'
			AND user_id = test_data.test_user1_id;
		
		ASSERT event_count = 1, 'Should detect 1 brute force attempt';
		
		-- Check unusual access pattern
		SELECT * INTO threat_record
		FROM authorization.detect_suspicious_activity(10, 3)
		WHERE threat_type = 'UNUSUAL_ACCESS_PATTERN'
			AND user_id = test_data.test_user2_id;
		
		ASSERT threat_record IS NOT NULL, 'Should detect unusual access pattern';
		ASSERT threat_record.event_count >= 5, 'Should show multiple org accesses';
		
		RAISE NOTICE 'TEST PASSED: Suspicious activity detection';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Threat detection - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events 
	WHERE event_category IN ('SECURITY', 'AUTHORIZATION', 'TEST_CATEGORY')
		OR details->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Security Event Monitoring';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Security Event Monitoring';
	END IF;
END $$;

-- ============================================================================
-- Test: Token Cleanup
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	cleanup_result		RECORD;
	token_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Token Cleanup - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Add test tokens with various expiration times
	INSERT INTO authorization.token_blacklist (token_jti, user_id, expires_at, reason)
	VALUES 
		('cleanup_expired_1', test_data.test_user1_id, CURRENT_TIMESTAMP - INTERVAL '2 hours', 'Test expired 1'),
		('cleanup_expired_2', test_data.test_user1_id, CURRENT_TIMESTAMP - INTERVAL '1 hour', 'Test expired 2'),
		('cleanup_active_1', test_data.test_user2_id, CURRENT_TIMESTAMP + INTERVAL '1 hour', 'Test active 1'),
		('cleanup_active_2', test_data.test_user2_id, CURRENT_TIMESTAMP + INTERVAL '2 hours', 'Test active 2');
	
	-- Test 1: Cleanup expired tokens
	BEGIN
		SELECT * INTO cleanup_result
		FROM authorization.cleanup_expired_tokens();
		
		ASSERT cleanup_result.deleted_count = 2, 'Should delete 2 expired tokens';
		
		-- Verify expired tokens are gone
		SELECT COUNT(*) INTO token_count
		FROM authorization.token_blacklist
		WHERE token_jti LIKE 'cleanup_expired_%';
		
		ASSERT token_count = 0, 'Expired tokens should be deleted';
		
		-- Verify active tokens remain
		SELECT COUNT(*) INTO token_count
		FROM authorization.token_blacklist
		WHERE token_jti LIKE 'cleanup_active_%';
		
		ASSERT token_count = 2, 'Active tokens should remain';
		
		-- Verify cleanup was audited
		ASSERT EXISTS(
			SELECT 1 FROM authorization.audit_events
			WHERE event_type = 'TOKEN_CLEANUP'
				AND details->>'deleted_count' = '2'
		), 'Cleanup should be audited';
		
		RAISE NOTICE 'TEST PASSED: Token cleanup';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Token cleanup - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Cleanup with no expired tokens
	BEGIN
		SELECT * INTO cleanup_result
		FROM authorization.cleanup_expired_tokens();
		
		ASSERT cleanup_result.deleted_count = 0, 'Should delete 0 tokens on second run';
		
		RAISE NOTICE 'TEST PASSED: No tokens to cleanup';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Empty cleanup - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE event_type = 'TOKEN_CLEANUP';
	DELETE FROM authorization.token_blacklist WHERE token_jti LIKE 'cleanup_%';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Token Cleanup';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Token Cleanup';
	END IF;
END $$;

-- ============================================================================
-- Test: Cache Invalidation
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Cache Invalidation - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_token_audit_test_data();
	
	-- Test 1: Cache invalidation logging
	BEGIN
		-- Call cache invalidation
		PERFORM authorization.invalidate_user_permission_cache(
			test_data.test_user1_id,
			test_data.org_id
		);
		
		-- Verify audit event created
		ASSERT EXISTS(
			SELECT 1 FROM authorization.audit_events
			WHERE event_type = 'CACHE_INVALIDATED'
				AND user_id = test_data.test_user1_id
				AND organization_id = test_data.org_id
				AND details->>'cache_type' = 'user_permissions'
		), 'Cache invalidation should be audited';
		
		RAISE NOTICE 'TEST PASSED: Cache invalidation with org';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Cache invalidation - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Cache invalidation without org
	BEGIN
		-- Call without org ID
		PERFORM authorization.invalidate_user_permission_cache(
			test_data.test_user2_id,
			NULL
		);
		
		-- Verify audit event created
		ASSERT EXISTS(
			SELECT 1 FROM authorization.audit_events
			WHERE event_type = 'CACHE_INVALIDATED'
				AND user_id = test_data.test_user2_id
				AND organization_id IS NULL
		), 'Cache invalidation without org should be audited';
		
		RAISE NOTICE 'TEST PASSED: Cache invalidation without org';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Cache invalidation no org - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE event_type = 'CACHE_INVALIDATED';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Cache Invalidation';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Cache Invalidation';
	END IF;
END $$;

-- Final cleanup
DROP FUNCTION test.setup_token_audit_test_data();

RAISE NOTICE 'All token and audit management tests completed';