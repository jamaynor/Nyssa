-- ============================================================================
-- File: 02_test_permission_resolution.sql
-- Description: Test suite for permission resolution functions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Enable detailed error messages
\set VERBOSITY verbose

-- ============================================================================
-- Test Helper Functions
-- ============================================================================
CREATE OR REPLACE FUNCTION test.setup_permission_test_data()
RETURNS TABLE(
	test_user_id		UUID,
	parent_org_id		UUID,
	child_org_id		UUID,
	parent_role_id		UUID,
	child_role_id		UUID,
	perm_read_id		UUID,
	perm_write_id		UUID
) AS $$
DECLARE
	v_test_user_id		UUID;
	v_parent_org_id		UUID;
	v_child_org_id		UUID;
	v_parent_role_id	UUID;
	v_child_role_id		UUID;
	v_perm_read_id		UUID;
	v_perm_write_id		UUID;
BEGIN
	-- Create test user
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('perm_test@example.com', 'Permission', 'Tester', '{"test_context": true}')
	RETURNING id INTO v_test_user_id;
	
	-- Create parent and child organizations
	INSERT INTO authorization.organizations (name, display_name, path, metadata)
	VALUES ('PermTestCorp', 'Permission Test Corp', 'permtestcorp', '{"test_context": true}')
	RETURNING id INTO v_parent_org_id;
	
	INSERT INTO authorization.organizations (name, display_name, parent_id, path, metadata)
	VALUES ('PermTestDept', 'Permission Test Dept', v_parent_org_id, 'permtestcorp.permtestdept', '{"test_context": true}')
	RETURNING id INTO v_child_org_id;
	
	-- Create permissions
	INSERT INTO authorization.permissions (permission, display_name, category, metadata)
	VALUES ('test:read', 'Test Read Permission', 'table', '{"test_context": true}')
	RETURNING id INTO v_perm_read_id;
	
	INSERT INTO authorization.permissions (permission, display_name, category, metadata)
	VALUES ('test:write', 'Test Write Permission', 'table', '{"test_context": true}')
	RETURNING id INTO v_perm_write_id;
	
	-- Create roles
	INSERT INTO authorization.roles (organization_id, name, display_name, is_inheritable, priority, metadata)
	VALUES (v_parent_org_id, 'parent_admin', 'Parent Admin', true, 100, '{"test_context": true}')
	RETURNING id INTO v_parent_role_id;
	
	INSERT INTO authorization.roles (organization_id, name, display_name, is_inheritable, priority, metadata)
	VALUES (v_child_org_id, 'child_user', 'Child User', false, 50, '{"test_context": true}')
	RETURNING id INTO v_child_role_id;
	
	-- Add user to organizations
	INSERT INTO authorization.organization_memberships (user_id, organization_id, metadata)
	VALUES 
		(v_test_user_id, v_parent_org_id, '{"test_context": true}'),
		(v_test_user_id, v_child_org_id, '{"test_context": true}');
	
	RETURN QUERY SELECT 
		v_test_user_id,
		v_parent_org_id,
		v_child_org_id,
		v_parent_role_id,
		v_child_role_id,
		v_perm_read_id,
		v_perm_write_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Test: Resolve User Permissions
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	perm_record			RECORD;
	perm_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Resolve User Permissions - Starting';
	
	-- Setup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	SELECT * INTO test_data FROM test.setup_permission_test_data();
	
	-- Test 1: No permissions without role assignment
	BEGIN
		SELECT COUNT(*) INTO perm_count
		FROM authorization.resolve_user_permissions(
			test_data.test_user_id,
			test_data.child_org_id,
			true,
			NULL
		);
		
		ASSERT perm_count = 0, 'Should have no permissions without role';
		
		RAISE NOTICE 'TEST PASSED: No permissions without role';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: No permissions without role - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Direct permissions in organization
	BEGIN
		-- Assign role and permissions
		INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
		VALUES (test_data.child_role_id, test_data.perm_read_id, '{"test_context": true}');
		
		INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
		VALUES (test_data.test_user_id, test_data.child_role_id, test_data.child_org_id, 
			test_data.test_user_id, '{"test_context": true}');
		
		SELECT * INTO perm_record
		FROM authorization.resolve_user_permissions(
			test_data.test_user_id,
			test_data.child_org_id,
			true,
			NULL
		);
		
		ASSERT perm_record.permission = 'test:read', 'Should have read permission';
		ASSERT perm_record.source = 'direct', 'Should be direct permission';
		ASSERT perm_record.role_name = 'child_user', 'Should show correct role';
		
		RAISE NOTICE 'TEST PASSED: Direct permissions resolved';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Direct permissions - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Inherited permissions from parent
	BEGIN
		-- Add inheritable role in parent org
		INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
		VALUES (test_data.parent_role_id, test_data.perm_write_id, '{"test_context": true}');
		
		INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
		VALUES (test_data.test_user_id, test_data.parent_role_id, test_data.parent_org_id,
			test_data.test_user_id, '{"test_context": true}');
		
		SELECT COUNT(*) INTO perm_count
		FROM authorization.resolve_user_permissions(
			test_data.test_user_id,
			test_data.child_org_id,
			true,  -- Include inherited
			NULL
		)
		WHERE source = 'inherited';
		
		ASSERT perm_count = 1, 'Should have 1 inherited permission';
		
		RAISE NOTICE 'TEST PASSED: Inherited permissions resolved';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Inherited permissions - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Permission filtering
	BEGIN
		SELECT COUNT(*) INTO perm_count
		FROM authorization.resolve_user_permissions(
			test_data.test_user_id,
			test_data.child_org_id,
			true,
			'test:wr'  -- Filter for write permissions
		);
		
		ASSERT perm_count = 1, 'Should have 1 filtered permission';
		
		RAISE NOTICE 'TEST PASSED: Permission filtering works';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Permission filtering - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Resolve User Permissions';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Resolve User Permissions';
	END IF;
END $$;

-- ============================================================================
-- Test: Check User Permission
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	perm_check			RECORD;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Check User Permission - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_permission_test_data();
	
	-- Test 1: Permission denied without role
	BEGIN
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			test_data.child_org_id,
			'test:read',
			true
		);
		
		ASSERT perm_check.allowed = false, 'Should not have permission';
		ASSERT perm_check.source = 'permission_denied', 'Should show permission denied';
		
		RAISE NOTICE 'TEST PASSED: Permission denied without role';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Permission denied - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Direct permission allowed
	BEGIN
		-- Grant permission
		INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
		VALUES (test_data.child_role_id, test_data.perm_read_id, '{"test_context": true}');
		
		INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
		VALUES (test_data.test_user_id, test_data.child_role_id, test_data.child_org_id,
			test_data.test_user_id, '{"test_context": true}');
		
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			test_data.child_org_id,
			'test:read',
			true
		);
		
		ASSERT perm_check.allowed = true, 'Should have permission';
		ASSERT perm_check.source = 'direct', 'Should be direct permission';
		
		RAISE NOTICE 'TEST PASSED: Direct permission allowed';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Direct permission - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Expired permission denied
	BEGIN
		-- Update role to expired
		UPDATE authorization.user_roles 
		SET expires_at = CURRENT_TIMESTAMP - INTERVAL '1 hour'
		WHERE user_id = test_data.test_user_id 
			AND role_id = test_data.child_role_id;
		
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			test_data.child_org_id,
			'test:read',
			true
		);
		
		ASSERT perm_check.allowed = false, 'Should not have expired permission';
		
		RAISE NOTICE 'TEST PASSED: Expired permission denied';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Expired permission - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Inactive organization denies access
	BEGIN
		-- Reactivate role
		UPDATE authorization.user_roles 
		SET expires_at = NULL
		WHERE user_id = test_data.test_user_id 
			AND role_id = test_data.child_role_id;
		
		-- Deactivate organization
		UPDATE authorization.organizations 
		SET is_active = false 
		WHERE id = test_data.child_org_id;
		
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			test_data.child_org_id,
			'test:read',
			true
		);
		
		ASSERT perm_check.allowed = false, 'Should not have permission in inactive org';
		ASSERT perm_check.source = 'organization_not_found', 'Should show org not found';
		
		RAISE NOTICE 'TEST PASSED: Inactive organization denies access';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Inactive org - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Check User Permission';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Check User Permission';
	END IF;
END $$;

-- ============================================================================
-- Test: Bulk Permission Check
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	perm_results		RECORD;
	allowed_count		INTEGER;
	denied_count		INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Bulk Permission Check - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_permission_test_data();
	
	-- Create additional permissions
	INSERT INTO authorization.permissions (permission, display_name, category, metadata)
	VALUES 
		('test:delete', 'Test Delete Permission', 'table', '{"test_context": true}'),
		('test:admin', 'Test Admin Permission', 'table', '{"test_context": true}');
	
	-- Grant some permissions
	INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
	SELECT test_data.child_role_id, id, '{"test_context": true}'
	FROM authorization.permissions
	WHERE permission IN ('test:read', 'test:write')
		AND metadata->>'test_context' = 'true';
	
	INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
	VALUES (test_data.test_user_id, test_data.child_role_id, test_data.child_org_id,
		test_data.test_user_id, '{"test_context": true}');
	
	-- Test 1: Check multiple permissions
	BEGIN
		SELECT 
			COUNT(*) FILTER (WHERE allowed = true) as allowed,
			COUNT(*) FILTER (WHERE allowed = false) as denied
		INTO allowed_count, denied_count
		FROM authorization.check_user_permissions_bulk(
			test_data.test_user_id,
			test_data.child_org_id,
			ARRAY['test:read', 'test:write', 'test:delete', 'test:admin'],
			true
		);
		
		ASSERT allowed_count = 2, 'Should have 2 allowed permissions';
		ASSERT denied_count = 2, 'Should have 2 denied permissions';
		
		RAISE NOTICE 'TEST PASSED: Bulk permission check counts correct';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Bulk check counts - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Verify specific permission results
	BEGIN
		SELECT * INTO perm_results
		FROM authorization.check_user_permissions_bulk(
			test_data.test_user_id,
			test_data.child_org_id,
			ARRAY['test:read', 'test:admin'],
			true
		)
		WHERE permission = 'test:read';
		
		ASSERT perm_results.allowed = true, 'Read permission should be allowed';
		ASSERT perm_results.source = 'direct', 'Read should be direct';
		
		SELECT * INTO perm_results
		FROM authorization.check_user_permissions_bulk(
			test_data.test_user_id,
			test_data.child_org_id,
			ARRAY['test:read', 'test:admin'],
			true
		)
		WHERE permission = 'test:admin';
		
		ASSERT perm_results.allowed = false, 'Admin permission should be denied';
		ASSERT perm_results.source = 'permission_denied', 'Admin should be denied';
		
		RAISE NOTICE 'TEST PASSED: Individual permission results correct';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Individual results - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Empty permission array
	BEGIN
		SELECT COUNT(*) INTO allowed_count
		FROM authorization.check_user_permissions_bulk(
			test_data.test_user_id,
			test_data.child_org_id,
			ARRAY[]::VARCHAR[],
			true
		);
		
		ASSERT allowed_count = 0, 'Empty array should return no results';
		
		RAISE NOTICE 'TEST PASSED: Empty array handled correctly';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Empty array - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Bulk Permission Check';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Bulk Permission Check';
	END IF;
END $$;

-- ============================================================================
-- Test: Permission Inheritance
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	grandchild_org_id	UUID;
	perm_check			RECORD;
	perm_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Permission Inheritance - Starting';
	
	-- Setup with additional grandchild org
	SELECT * INTO test_data FROM test.setup_permission_test_data();
	
	INSERT INTO authorization.organizations (name, display_name, parent_id, path, metadata)
	VALUES ('GrandchildDept', 'Grandchild Dept', test_data.child_org_id, 
		'permtestcorp.permtestdept.grandchilddept', '{"test_context": true}')
	RETURNING id INTO grandchild_org_id;
	
	INSERT INTO authorization.organization_memberships (user_id, organization_id, metadata)
	VALUES (test_data.test_user_id, grandchild_org_id, '{"test_context": true}');
	
	-- Test 1: Inheritable role permissions cascade down
	BEGIN
		-- Grant inheritable role at parent
		INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
		VALUES (test_data.parent_role_id, test_data.perm_write_id, '{"test_context": true}');
		
		INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
		VALUES (test_data.test_user_id, test_data.parent_role_id, test_data.parent_org_id,
			test_data.test_user_id, '{"test_context": true}');
		
		-- Check permission at grandchild level
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			grandchild_org_id,
			'test:write',
			true  -- Include inherited
		);
		
		ASSERT perm_check.allowed = true, 'Should inherit permission from grandparent';
		ASSERT perm_check.source = 'inherited', 'Should be inherited permission';
		
		RAISE NOTICE 'TEST PASSED: Permission inherited through hierarchy';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Permission inheritance - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Non-inheritable role doesn't cascade
	BEGIN
		-- Make role non-inheritable
		UPDATE authorization.roles 
		SET is_inheritable = false 
		WHERE id = test_data.parent_role_id;
		
		-- Check permission again
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			grandchild_org_id,
			'test:write',
			true
		);
		
		ASSERT perm_check.allowed = false, 'Should not inherit non-inheritable permission';
		
		RAISE NOTICE 'TEST PASSED: Non-inheritable role blocked correctly';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Non-inheritable role - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Direct permission overrides inheritance
	BEGIN
		-- Add direct conflicting permission
		INSERT INTO authorization.role_permissions (role_id, permission_id, metadata)
		VALUES (test_data.child_role_id, test_data.perm_write_id, '{"test_context": true}');
		
		INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
		VALUES (test_data.test_user_id, test_data.child_role_id, grandchild_org_id,
			test_data.test_user_id, '{"test_context": true}');
		
		-- Make parent role inheritable again
		UPDATE authorization.roles 
		SET is_inheritable = true, priority = 50
		WHERE id = test_data.parent_role_id;
		
		-- Make child role higher priority
		UPDATE authorization.roles 
		SET priority = 100
		WHERE id = test_data.child_role_id;
		
		SELECT * INTO perm_check
		FROM authorization.check_user_permission(
			test_data.test_user_id,
			grandchild_org_id,
			'test:write',
			true
		);
		
		ASSERT perm_check.allowed = true, 'Should have permission';
		ASSERT perm_check.source = 'direct', 'Direct should override inherited';
		
		RAISE NOTICE 'TEST PASSED: Direct permission overrides inheritance';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Direct override - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Permission Inheritance';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Permission Inheritance';
	END IF;
END $$;

-- Final cleanup
DROP FUNCTION test.setup_permission_test_data();

RAISE NOTICE 'All permission resolution tests completed';