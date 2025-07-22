-- ============================================================================
-- File: 01_create_schema.sql
-- Description: Creates the auth schema and enables required PostgreSQL extensions
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

-- Create auth schema namespace
CREATE SCHEMA IF NOT EXISTS authz;

-- Set search path for this session
SET search_path TO authz, public;

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";    -- For UUID generation
CREATE EXTENSION IF NOT EXISTS ltree;           -- For hierarchical organization paths
CREATE EXTENSION IF NOT EXISTS pgcrypto;        -- For encryption capabilities
CREATE EXTENSION IF NOT EXISTS pg_trgm;         -- For text search optimization
CREATE EXTENSION IF NOT EXISTS btree_gist;      -- For advanced indexing

-- Grant usage on schema to application role (adjust role name as needed)
-- GRANT USAGE ON SCHEMA authz TO rbac_app_user;

-- Add schema comment
COMMENT ON SCHEMA authz IS 'Multi-organization RBAC system schema for role-based access control';

