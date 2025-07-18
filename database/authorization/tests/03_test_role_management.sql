-- ============================================================================
-- File: 03_test_role_management.sql
-- Description: Test suite for role management functions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authorization, public;

-- Enable detailed error messages
\set VERBOSITY verbose

-- ============================================================================
-- Test Helper Functions
-- ============================================================================
CREATE OR REPLACE FUNCTION test.setup_role_test_data()
RETURNS TABLE(
	admin_user_id		UUID,
	test_user1_id		UUID,
	test_user2_id		UUID,
	org_id				UUID,
	role1_id			UUID,
	role2_id			UUID,
	perm1_id			UUID,
	perm2_id			UUID
) AS $$
DECLARE
	v_admin_user_id		UUID;
	v_test_user1_id		UUID;
	v_test_user2_id		UUID;
	v_org_id			UUID;
	v_role1_id			UUID;
	v_role2_id			UUID;
	v_perm1_id			UUID;
	v_perm2_id			UUID;
BEGIN
	-- Create users
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('admin@roletest.com', 'Admin', 'User', '{"test_context": true}')
	RETURNING id INTO v_admin_user_id;
	
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('user1@roletest.com', 'Test', 'User1', '{"test_context": true}')
	RETURNING id INTO v_test_user1_id;
	
	INSERT INTO authorization.users (email, first_name, last_name, metadata)
	VALUES ('user2@roletest.com', 'Test', 'User2', '{"test_context": true}')
	RETURNING id INTO v_test_user2_id;
	
	-- Create organization
	INSERT INTO authorization.organizations (name, display_name, path, metadata)
	VALUES ('RoleTestOrg', 'Role Test Organization', 'roletestorg', '{"test_context": true}')
	RETURNING id INTO v_org_id;
	
	-- Add users as members
	INSERT INTO authorization.organization_memberships (user_id, organization_id, metadata)
	VALUES 
		(v_admin_user_id, v_org_id, '{"test_context": true}'),
		(v_test_user1_id, v_org_id, '{"test_context": true}'),
		(v_test_user2_id, v_org_id, '{"test_context": true}');
	
	-- Create roles
	INSERT INTO authorization.roles (organization_id, name, display_name, priority, metadata)
	VALUES (v_org_id, 'test_admin', 'Test Admin', 100, '{"test_context": true}')
	RETURNING id INTO v_role1_id;
	
	INSERT INTO authorization.roles (organization_id, name, display_name, priority, metadata)
	VALUES (v_org_id, 'test_user', 'Test User', 50, '{"test_context": true}')
	RETURNING id INTO v_role2_id;
	
	-- Create permissions
	INSERT INTO authorization.permissions (permission, display_name, category, metadata)
	VALUES ('roles:manage', 'Manage Roles', 'system', '{"test_context": true}')
	RETURNING id INTO v_perm1_id;
	
	INSERT INTO authorization.permissions (permission, display_name, category, metadata)
	VALUES ('users:view', 'View Users', 'table', '{"test_context": true}')
	RETURNING id INTO v_perm2_id;
	
	RETURN QUERY SELECT 
		v_admin_user_id,
		v_test_user1_id,
		v_test_user2_id,
		v_org_id,
		v_role1_id,
		v_role2_id,
		v_perm1_id,
		v_perm2_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Test: Assign User Role
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	assignment_result	RECORD;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Assign User Role - Starting';
	
	-- Setup
	DELETE FROM authorization.audit_events WHERE details->>'test_context' = 'true';
	DELETE FROM authorization.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.role_permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.permissions WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authorization.users WHERE metadata->>'test_context' = 'true';
	
	SELECT * INTO test_data FROM test.setup_role_test_data();
	
	-- Test 1: Successful role assignment
	BEGIN
		SELECT * INTO assignment_result
		FROM authorization.assign_user_role(
			test_data.test_user1_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			NULL,  -- No expiration
			'{}',  -- No conditions
			'{"test_context": true}'
		);
		
		ASSERT assignment_result.assignment_id IS NOT NULL, 'Assignment ID should not be null';
		ASSERT assignment_result.assigned_at IS NOT NULL, 'Assignment time should not be null';
		
		-- Verify assignment exists
		ASSERT EXISTS(
			SELECT 1 FROM authorization.user_roles
			WHERE id = assignment_result.assignment_id
				AND user_id = test_data.test_user1_id
				AND role_id = test_data.role1_id
				AND is_active = true
		), 'Role assignment should exist and be active';
		
		RAISE NOTICE 'TEST PASSED: Successful role assignment';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Role assignment - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Duplicate assignment should fail
	BEGIN
		SELECT * INTO assignment_result
		FROM authorization.assign_user_role(
			test_data.test_user1_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			NULL, '{}', '{"test_context": true}'
		);
		
		RAISE NOTICE 'TEST FAILED: Duplicate assignment should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Duplicate assignment correctly rejected - %', SQLERRM;
	END;
	
	-- Test 3: Assignment with expiration
	BEGIN
		SELECT * INTO assignment_result
		FROM authorization.assign_user_role(
			test_data.test_user2_id,
			test_data.role2_id,
			test_data.org_id,
			test_data.admin_user_id,
			CURRENT_TIMESTAMP + INTERVAL '30 days',
			'{}',
			'{"test_context": true}'
		);
		
		-- Verify expiration is set
		ASSERT EXISTS(
			SELECT 1 FROM authorization.user_roles
			WHERE id = assignment_result.assignment_id
				AND expires_at IS NOT NULL
				AND expires_at > CURRENT_TIMESTAMP
		), 'Expiration should be set in the future';
		
		RAISE NOTICE 'TEST PASSED: Assignment with expiration';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Assignment with expiration - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Assignment to non-member should fail
	BEGIN
		-- Remove membership
		DELETE FROM authorization.organization_memberships
		WHERE user_id = test_data.test_user1_id 
			AND organization_id = test_data.org_id;
		
		SELECT * INTO assignment_result
		FROM authorization.assign_user_role(
			test_data.test_user1_id,
			test_data.role2_id,
			test_data.org_id,
			test_data.admin_user_id,
			NULL, '{}', '{"test_context": true}'
		);
		
		RAISE NOTICE 'TEST FAILED: Non-member assignment should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Non-member assignment correctly rejected - %', SQLERRM;
	END;
	
	-- Test 5: Assignment with past expiration should fail
	BEGIN
		SELECT * INTO assignment_result
		FROM authorization.assign_user_role(
			test_data.test_user2_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			CURRENT_TIMESTAMP - INTERVAL '1 hour',  -- Past date
			'{}',
			'{"test_context": true}'
		);
		
		RAISE NOTICE 'TEST FAILED: Past expiration should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Past expiration correctly rejected - %', SQLERRM;
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
		RAISE NOTICE 'TEST SUITE PASSED: Assign User Role';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Assign User Role';
	END IF;
END $$;

-- ============================================================================
-- Test: Revoke User Role
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	assignment_id		UUID;
	revoke_result		BOOLEAN;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Revoke User Role - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_role_test_data();
	
	-- Assign role first
	SELECT assignment_id INTO assignment_id
	FROM authorization.assign_user_role(
		test_data.test_user1_id,
		test_data.role1_id,
		test_data.org_id,
		test_data.admin_user_id,
		NULL, '{}', '{"test_context": true}'
	);
	
	-- Test 1: Successful revocation
	BEGIN
		revoke_result := authorization.revoke_user_role(
			test_data.test_user1_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			'Testing revocation'
		);
		
		ASSERT revoke_result = true, 'Revocation should return true';
		
		-- Verify assignment is inactive
		ASSERT NOT EXISTS(
			SELECT 1 FROM authorization.user_roles
			WHERE user_id = test_data.test_user1_id
				AND role_id = test_data.role1_id
				AND organization_id = test_data.org_id
				AND is_active = true
		), 'Role should be inactive after revocation';
		
		-- Verify metadata contains revocation info
		ASSERT EXISTS(
			SELECT 1 FROM authorization.user_roles
			WHERE id = assignment_id
				AND metadata->>'revoked_by' IS NOT NULL
				AND metadata->>'revocation_reason' = 'Testing revocation'
		), 'Revocation metadata should be stored';
		
		RAISE NOTICE 'TEST PASSED: Successful revocation';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Revocation - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Revoke already revoked role should fail
	BEGIN
		revoke_result := authorization.revoke_user_role(
			test_data.test_user1_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			'Duplicate revocation'
		);
		
		RAISE NOTICE 'TEST FAILED: Duplicate revocation should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Duplicate revocation correctly rejected - %', SQLERRM;
	END;
	
	-- Test 3: Revoke non-existent assignment should fail
	BEGIN
		revoke_result := authorization.revoke_user_role(
			test_data.test_user2_id,
			test_data.role1_id,
			test_data.org_id,
			test_data.admin_user_id,
			'Non-existent'
		);
		
		RAISE NOTICE 'TEST FAILED: Non-existent revocation should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Non-existent revocation correctly rejected - %', SQLERRM;
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
		RAISE NOTICE 'TEST SUITE PASSED: Revoke User Role';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Revoke User Role';
	END IF;
END $$;

-- ============================================================================
-- Test: Get User Roles
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	parent_org_id		UUID;
	role_record			RECORD;
	role_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Get User Roles - Starting';
	
	-- Setup with parent organization
	SELECT * INTO test_data FROM test.setup_role_test_data();
	
	-- Create parent org
	INSERT INTO authorization.organizations (name, display_name, path, metadata)
	VALUES ('ParentOrg', 'Parent Organization', 'parentorg', '{"test_context": true}')
	RETURNING id INTO parent_org_id;
	
	-- Update child org to have parent
	UPDATE authorization.organizations 
	SET parent_id = parent_org_id, path = 'parentorg.roletestorg'
	WHERE id = test_data.org_id;
	
	-- Create inheritable role in parent
	INSERT INTO authorization.roles (organization_id, name, display_name, is_inheritable, priority, metadata)
	VALUES (parent_org_id, 'parent_manager', 'Parent Manager', true, 150, '{"test_context": true}');
	
	-- Add user to parent org
	INSERT INTO authorization.organization_memberships (user_id, organization_id, metadata)
	VALUES (test_data.test_user1_id, parent_org_id, '{"test_context": true}');
	
	-- Assign roles
	INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, metadata)
	VALUES 
		(test_data.test_user1_id, test_data.role1_id, test_data.org_id, 
			test_data.admin_user_id, '{"test_context": true}'),
		(test_data.test_user1_id, 
			(SELECT id FROM authorization.roles WHERE name = 'parent_manager'), 
			parent_org_id, test_data.admin_user_id, '{"test_context": true}');
	
	-- Test 1: Get all roles for user
	BEGIN
		SELECT COUNT(*) INTO role_count
		FROM authorization.get_user_roles(
			test_data.test_user1_id,
			NULL,  -- All organizations
			true   -- Include inherited
		);
		
		ASSERT role_count = 2, 'Should have 2 roles total';
		
		RAISE NOTICE 'TEST PASSED: Get all user roles';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get all roles - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Get roles for specific organization (direct only)
	BEGIN
		SELECT COUNT(*) INTO role_count
		FROM authorization.get_user_roles(
			test_data.test_user1_id,
			test_data.org_id,
			false  -- No inheritance
		);
		
		ASSERT role_count = 1, 'Should have 1 direct role in child org';
		
		RAISE NOTICE 'TEST PASSED: Get direct roles only';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get direct roles - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Check inherited roles
	BEGIN
		SELECT * INTO role_record
		FROM authorization.get_user_roles(
			test_data.test_user1_id,
			test_data.org_id,
			true  -- Include inherited
		)
		WHERE source = 'inherited';
		
		ASSERT role_record.role_name = 'parent_manager', 'Should inherit parent role';
		ASSERT role_record.is_inheritable = true, 'Inherited role should be inheritable';
		ASSERT role_record.organization_id = parent_org_id, 'Should show parent org';
		
		RAISE NOTICE 'TEST PASSED: Inherited roles included';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Inherited roles - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Expired roles not included
	BEGIN
		-- Set role to expired
		UPDATE authorization.user_roles
		SET expires_at = CURRENT_TIMESTAMP - INTERVAL '1 hour'
		WHERE user_id = test_data.test_user1_id 
			AND role_id = test_data.role1_id;
		
		SELECT COUNT(*) INTO role_count
		FROM authorization.get_user_roles(
			test_data.test_user1_id,
			test_data.org_id,
			false
		);
		
		ASSERT role_count = 0, 'Should not include expired roles';
		
		RAISE NOTICE 'TEST PASSED: Expired roles excluded';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Expired roles - %', SQLERRM;
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
		RAISE NOTICE 'TEST SUITE PASSED: Get User Roles';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Get User Roles';
	END IF;
END $$;

-- ============================================================================
-- Test: Role Permission Management
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	add_result			BOOLEAN;
	remove_result		BOOLEAN;
	perm_count			INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Role Permission Management - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_role_test_data();
	
	-- Test 1: Add permission to role
	BEGIN
		add_result := authorization.add_permission_to_role(
			test_data.role1_id,
			'roles:manage',
			test_data.admin_user_id,
			'{}',
			'{"test_context": true}'
		);
		
		ASSERT add_result = true, 'Add permission should return true';
		
		-- Verify permission added
		ASSERT EXISTS(
			SELECT 1 FROM authorization.role_permissions rp
			JOIN authorization.permissions p ON rp.permission_id = p.id
			WHERE rp.role_id = test_data.role1_id
				AND p.permission = 'roles:manage'
		), 'Permission should be added to role';
		
		RAISE NOTICE 'TEST PASSED: Add permission to role';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Add permission - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Add duplicate permission should fail
	BEGIN
		add_result := authorization.add_permission_to_role(
			test_data.role1_id,
			'roles:manage',
			test_data.admin_user_id,
			'{}',
			'{"test_context": true}'
		);
		
		RAISE NOTICE 'TEST FAILED: Duplicate permission should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Duplicate permission correctly rejected - %', SQLERRM;
	END;
	
	-- Test 3: Add invalid permission should fail
	BEGIN
		add_result := authorization.add_permission_to_role(
			test_data.role1_id,
			'invalid:permission',
			test_data.admin_user_id,
			'{}',
			'{"test_context": true}'
		);
		
		RAISE NOTICE 'TEST FAILED: Invalid permission should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Invalid permission correctly rejected - %', SQLERRM;
	END;
	
	-- Test 4: Remove permission from role
	BEGIN
		remove_result := authorization.remove_permission_from_role(
			test_data.role1_id,
			'roles:manage',
			test_data.admin_user_id
		);
		
		ASSERT remove_result = true, 'Remove permission should return true';
		
		-- Verify permission removed
		ASSERT NOT EXISTS(
			SELECT 1 FROM authorization.role_permissions rp
			JOIN authorization.permissions p ON rp.permission_id = p.id
			WHERE rp.role_id = test_data.role1_id
				AND p.permission = 'roles:manage'
		), 'Permission should be removed from role';
		
		RAISE NOTICE 'TEST PASSED: Remove permission from role';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Remove permission - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 5: Remove non-existent permission should fail
	BEGIN
		remove_result := authorization.remove_permission_from_role(
			test_data.role1_id,
			'roles:manage',
			test_data.admin_user_id
		);
		
		RAISE NOTICE 'TEST FAILED: Remove non-existent permission should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Remove non-existent permission correctly rejected - %', SQLERRM;
	END;
	
	-- Test 6: Get role permissions
	BEGIN
		-- Add multiple permissions
		add_result := authorization.add_permission_to_role(
			test_data.role2_id, 'users:view', test_data.admin_user_id, 
			'{}', '{"test_context": true}'
		);
		
		SELECT COUNT(*) INTO perm_count
		FROM authorization.get_role_permissions(test_data.role2_id);
		
		ASSERT perm_count = 1, 'Should have 1 permission';
		
		RAISE NOTICE 'TEST PASSED: Get role permissions';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get role permissions - %', SQLERRM;
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
		RAISE NOTICE 'TEST SUITE PASSED: Role Permission Management';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Role Permission Management';
	END IF;
END $$;

-- ============================================================================
-- Test: Expire User Roles
-- ============================================================================
DO $$
DECLARE
	test_data			RECORD;
	expire_result		RECORD;
	active_count		INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Expire User Roles - Starting';
	
	-- Setup
	SELECT * INTO test_data FROM test.setup_role_test_data();
	
	-- Create roles with different expiration times
	INSERT INTO authorization.user_roles (user_id, role_id, organization_id, granted_by, expires_at, metadata)
	VALUES 
		-- Already expired
		(test_data.test_user1_id, test_data.role1_id, test_data.org_id, 
			test_data.admin_user_id, CURRENT_TIMESTAMP - INTERVAL '1 hour', '{"test_context": true}'),
		-- Expires in future
		(test_data.test_user1_id, test_data.role2_id, test_data.org_id,
			test_data.admin_user_id, CURRENT_TIMESTAMP + INTERVAL '1 hour', '{"test_context": true}'),
		-- Also expired
		(test_data.test_user2_id, test_data.role1_id, test_data.org_id,
			test_data.admin_user_id, CURRENT_TIMESTAMP - INTERVAL '2 hours', '{"test_context": true}'),
		-- No expiration
		(test_data.test_user2_id, test_data.role2_id, test_data.org_id,
			test_data.admin_user_id, NULL, '{"test_context": true}');
	
	-- Test 1: Expire roles
	BEGIN
		SELECT * INTO expire_result
		FROM authorization.expire_user_roles();
		
		ASSERT expire_result.expired_count = 2, 'Should expire 2 roles';
		ASSERT array_length(expire_result.user_ids, 1) = 2, 'Should affect 2 users';
		
		-- Verify expired roles are inactive
		SELECT COUNT(*) INTO active_count
		FROM authorization.user_roles
		WHERE metadata->>'test_context' = 'true'
			AND is_active = true;
		
		ASSERT active_count = 2, 'Should have 2 active roles remaining';
		
		-- Verify metadata updated
		ASSERT EXISTS(
			SELECT 1 FROM authorization.user_roles
			WHERE user_id = test_data.test_user1_id
				AND role_id = test_data.role1_id
				AND is_active = false
				AND metadata->>'auto_expired_at' IS NOT NULL
		), 'Expired role should have auto_expired_at metadata';
		
		RAISE NOTICE 'TEST PASSED: Expire user roles';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Expire roles - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Run again - no more to expire
	BEGIN
		SELECT * INTO expire_result
		FROM authorization.expire_user_roles();
		
		ASSERT expire_result.expired_count = 0, 'Should expire 0 roles on second run';
		
		RAISE NOTICE 'TEST PASSED: No duplicate expiration';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Duplicate expiration - %', SQLERRM;
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
		RAISE NOTICE 'TEST SUITE PASSED: Expire User Roles';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Expire User Roles';
	END IF;
END $$;

-- Final cleanup
DROP FUNCTION test.setup_role_test_data();

RAISE NOTICE 'All role management tests completed';