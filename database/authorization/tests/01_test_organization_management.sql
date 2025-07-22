-- ============================================================================
-- File: 01_test_organization_management.sql
-- Description: Test suite for organization management functions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO authz, public;

create schema if not exists test;

-- Enable detailed error messages
-- \set VERBOSITY verbose

-- ============================================================================
-- Test Helper Functions
-- ============================================================================
CREATE OR REPLACE FUNCTION test.cleanup_test_data()
RETURNS VOID AS $$
BEGIN
	-- Clean up test data in reverse dependency order
	-- Delete audit events related to test organizations and users
	DELETE FROM authz.audit_events WHERE 
		details->>'test_context' = 'true'
		OR organization_id IN (SELECT id FROM authz.organizations WHERE metadata->>'test_context' = 'true')
		OR user_id IN (SELECT id FROM authz.users WHERE metadata->>'test_context' = 'true');
	DELETE FROM authz.user_roles WHERE metadata->>'test_context' = 'true';
	DELETE FROM authz.organization_memberships WHERE metadata->>'test_context' = 'true';
	DELETE FROM authz.organizations WHERE metadata->>'test_context' = 'true';
	DELETE FROM authz.users WHERE metadata->>'test_context' = 'true';
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION test.create_test_user(p_email VARCHAR DEFAULT 'test@example.com')
RETURNS UUID AS $$
DECLARE
	user_id UUID;
BEGIN
	INSERT INTO authz.users (email, first_name, last_name, metadata)
	VALUES (p_email, 'Test', 'User', '{"test_context": true}')
	RETURNING id INTO user_id;
	
	RETURN user_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Test: Create Organization
-- ============================================================================
DO $$
DECLARE
	test_user_id		UUID;
	org_result			RECORD;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Create Organization - Starting';
	
	-- Setup
	PERFORM test.cleanup_test_data();
	test_user_id := test.create_test_user();
	
	-- Test 1: Create root organization
	BEGIN
		SELECT * INTO org_result FROM authz.create_organization(
			'Acme Corp',
			'ACME Corporation',
			'Test root organization',
			NULL,
			test_user_id,
			'{"test_context": true}'::jsonb
		);
		
		ASSERT org_result.id IS NOT NULL, 'Organization ID should not be null';
		ASSERT org_result.name = 'Acme Corp', 'Organization name should match';
		ASSERT org_result.path::text = 'acme_corp', 'Root org path should be sanitized name';
		
		RAISE NOTICE 'TEST PASSED: Create root organization';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Create root organization - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Create child organization
	BEGIN
		SELECT * INTO org_result FROM authz.create_organization(
			'Engineering',
			'Engineering Department',
			'Test child organization',
			org_result.id,  -- Parent from previous test
			test_user_id,
			'{"test_context": true}'::jsonb
		);
		
		ASSERT org_result.path::text = 'acme_corp.engineering', 'Child path should include parent';
		
		RAISE NOTICE 'TEST PASSED: Create child organization';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Create child organization - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Duplicate path should fail
	BEGIN
		SELECT * INTO org_result FROM authz.create_organization(
			'Acme Corp',
			'Duplicate Org',
			'Should fail',
			NULL,
			test_user_id,
			'{"test_context": true}'::jsonb
		);
		
		RAISE NOTICE 'TEST FAILED: Duplicate organization should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Duplicate organization correctly rejected - %', SQLERRM;
	END;
	
	-- Test 4: Invalid parent should fail
	BEGIN
		SELECT * INTO org_result FROM authz.create_organization(
			'Invalid Org',
			'Invalid Org',
			'Should fail',
			'00000000-0000-0000-0000-000000000000'::uuid,
			test_user_id,
			'{"test_context": true}'::jsonb
		);
		
		RAISE NOTICE 'TEST FAILED: Invalid parent should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Invalid parent correctly rejected - %', SQLERRM;
	END;
	
	-- Cleanup
	PERFORM test.cleanup_test_data();
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Create Organization';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Create Organization';
	END IF;
END $$;

-- ============================================================================
-- Test: Move Organization
-- ============================================================================
DO $$
DECLARE
	test_user_id		UUID;
	root_org_id			UUID;
	dept1_id			UUID;
	dept2_id			UUID;
	subdept_id			UUID;
	move_result			BOOLEAN;
	org_record			RECORD;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Move Organization - Starting';
	
	-- Setup
	PERFORM test.cleanup_test_data();
	test_user_id := test.create_test_user();
	
	-- Create test organization hierarchy
	SELECT id INTO root_org_id FROM authz.create_organization(
		'MoveTest Corp', 'MoveTest Corporation', 'Root', NULL, test_user_id, 
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO dept1_id FROM authz.create_organization(
		'Department 1', 'Dept 1', 'First dept', root_org_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO dept2_id FROM authz.create_organization(
		'Department 2', 'Dept 2', 'Second dept', root_org_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO subdept_id FROM authz.create_organization(
		'Sub Department', 'Sub Dept', 'Sub dept under dept1', dept1_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	-- Test 1: Move subdepartment to different parent
	BEGIN
		move_result := authz.move_organization(subdept_id, dept2_id, test_user_id);
		
		SELECT * INTO org_record FROM authz.organizations WHERE id = subdept_id;
		
		ASSERT move_result = true, 'Move should return true';
		ASSERT org_record.parent_id = dept2_id, 'Parent should be updated';
		ASSERT org_record.path::text = 'movetest_corp.department_2.sub_department', 
			'Path should reflect new parent';
		
		RAISE NOTICE 'TEST PASSED: Move organization to new parent';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Move organization - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Move to root (null parent)
	BEGIN
		move_result := authz.move_organization(subdept_id, NULL, test_user_id);
		
		SELECT * INTO org_record FROM authz.organizations WHERE id = subdept_id;
		
		ASSERT org_record.parent_id IS NULL, 'Parent should be null';
		ASSERT org_record.path::text = 'sub_department', 'Path should be root level';
		
		RAISE NOTICE 'TEST PASSED: Move organization to root';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Move to root - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Circular reference should fail
	BEGIN
		-- Move dept1 back under subdept to create circular reference
		move_result := authz.move_organization(subdept_id, dept1_id, test_user_id);
		move_result := authz.move_organization(dept1_id, subdept_id, test_user_id);
		
		RAISE NOTICE 'TEST FAILED: Circular reference should have failed';
		test_passed := false;
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST PASSED: Circular reference correctly rejected - %', SQLERRM;
	END;
	
	-- Cleanup
	PERFORM test.cleanup_test_data();
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Move Organization';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Move Organization';
	END IF;
END $$;

-- ============================================================================
-- Test: Get Organization Hierarchy
-- ============================================================================
DO $$
DECLARE
	test_user_id		UUID;
	root_org_id			UUID;
	dept_id				UUID;
	subdept_id			UUID;
	member_user_id		UUID;
	hierarchy_record	RECORD;
	record_count		INTEGER;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: Get Organization Hierarchy - Starting';
	
	-- Setup
	PERFORM test.cleanup_test_data();
	test_user_id := test.create_test_user();
	member_user_id := test.create_test_user('member@example.com');
	
	-- Create test hierarchy
	SELECT id INTO root_org_id FROM authz.create_organization(
		'Hierarchy Corp', 'Hierarchy Corporation', 'Root', NULL, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO dept_id FROM authz.create_organization(
		'Department', 'Department', 'Dept', root_org_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO subdept_id FROM authz.create_organization(
		'SubDepartment', 'Sub Department', 'SubDept', dept_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	-- Add member to department
	INSERT INTO authz.organization_memberships (user_id, organization_id, metadata)
	VALUES (member_user_id, dept_id, '{"test_context": true}'::jsonb);
	
	-- Test 1: Get full hierarchy without user filter
	BEGIN
		SELECT COUNT(*) INTO record_count 
		FROM authz.get_organization_hierarchy(
			NULL,  -- No user filter
			NULL,  -- No root filter
			NULL,  -- No depth limit
			false  -- Active only
		);
		
		ASSERT record_count >= 3, 'Should return at least 3 organizations';
		
		RAISE NOTICE 'TEST PASSED: Get full hierarchy';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get full hierarchy - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Get hierarchy from specific root
	BEGIN
		SELECT COUNT(*) INTO record_count 
		FROM authz.get_organization_hierarchy(
			NULL,
			root_org_id,  -- Start from our root
			NULL,
			false
		);
		
		ASSERT record_count = 3, 'Should return exactly 3 organizations in our tree';
		
		RAISE NOTICE 'TEST PASSED: Get hierarchy from specific root';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get hierarchy from root - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Get hierarchy with depth limit
	BEGIN
		SELECT COUNT(*) INTO record_count 
		FROM authz.get_organization_hierarchy(
			NULL,
			root_org_id,
			1,  -- Max depth of 1
			false
		);
		
		ASSERT record_count = 2, 'Should return root and direct children only';
		
		RAISE NOTICE 'TEST PASSED: Get hierarchy with depth limit';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Get hierarchy with depth - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 4: Check member access
	BEGIN
		SELECT * INTO hierarchy_record
		FROM authz.get_organization_hierarchy(
			member_user_id,  -- Filter by member
			NULL,
			NULL,
			false
		)
		WHERE id = dept_id;
		
		ASSERT hierarchy_record.is_direct_member = true, 'Should show direct membership';
		ASSERT hierarchy_record.has_access = true, 'Should have access';
		
		RAISE NOTICE 'TEST PASSED: Check member access in hierarchy';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Check member access - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	PERFORM test.cleanup_test_data();
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: Get Organization Hierarchy';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: Get Organization Hierarchy';
	END IF;
END $$;

-- ============================================================================
-- Test: User Organization Access
-- ============================================================================
DO $$
DECLARE
	test_user_id		UUID;
	member_user_id		UUID;
	root_org_id			UUID;
	dept_id				UUID;
	has_access			BOOLEAN;
	test_passed			BOOLEAN := true;
BEGIN
	RAISE NOTICE 'TEST: User Organization Access - Starting';
	
	-- Setup
	PERFORM test.cleanup_test_data();
	test_user_id := test.create_test_user();
	member_user_id := test.create_test_user('access@example.com');
	
	-- Create organizations
	SELECT id INTO root_org_id FROM authz.create_organization(
		'Access Corp', 'Access Corporation', 'Root', NULL, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	SELECT id INTO dept_id FROM authz.create_organization(
		'Access Dept', 'Access Department', 'Dept', root_org_id, test_user_id,
		'{"test_context": true}'::jsonb
	);
	
	-- Test 1: No access without membership
	BEGIN
		has_access := authz.user_has_organization_access(member_user_id, dept_id);
		
		ASSERT has_access = false, 'Should not have access without membership';
		
		RAISE NOTICE 'TEST PASSED: No access without membership';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: No access check - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 2: Access with direct membership
	BEGIN
		-- Add membership
		INSERT INTO authz.organization_memberships (user_id, organization_id, metadata)
		VALUES (member_user_id, dept_id, '{"test_context": true}'::jsonb);
		
		has_access := authz.user_has_organization_access(member_user_id, dept_id);
		
		ASSERT has_access = true, 'Should have access with membership';
		
		RAISE NOTICE 'TEST PASSED: Access with membership';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Access with membership - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Test 3: Access to inactive organization
	BEGIN
		-- Deactivate organization
		UPDATE authz.organizations SET is_active = false WHERE id = dept_id;
		
		has_access := authz.user_has_organization_access(member_user_id, dept_id);
		
		ASSERT has_access = false, 'Should not have access to inactive org';
		
		RAISE NOTICE 'TEST PASSED: No access to inactive organization';
	EXCEPTION WHEN OTHERS THEN
		RAISE NOTICE 'TEST FAILED: Inactive org access - %', SQLERRM;
		test_passed := false;
	END;
	
	-- Cleanup
	PERFORM test.cleanup_test_data();
	
	IF test_passed THEN
		RAISE NOTICE 'TEST SUITE PASSED: User Organization Access';
	ELSE
		RAISE EXCEPTION 'TEST SUITE FAILED: User Organization Access';
	END IF;
END $$;

-- Final cleanup
SELECT test.cleanup_test_data();
DROP FUNCTION test.cleanup_test_data();
DROP FUNCTION test.create_test_user(VARCHAR);

RAISE NOTICE 'All organization management tests completed';