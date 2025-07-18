-- ============================================================================
-- File: 09_audit_events.sql
-- Description: Audit events table for comprehensive system activity logging
-- Author: Platform Team
-- Created: January 2024
-- ============================================================================

SET search_path TO auth, public;

-- Drop existing table if needed (for clean installs)
-- DROP TABLE IF EXISTS authz.audit_events CASCADE;

-- Create main audit events table (will be partitioned by month)
CREATE TABLE authz.audit_events (
    id UUID DEFAULT uuid_generate_v4(),
    event_type VARCHAR(100) NOT NULL,
    event_category VARCHAR(50) NOT NULL,
    user_id UUID REFERENCES authz.users(id),
    organization_id UUID REFERENCES authz.organizations(id),
    resource_type VARCHAR(100),
    resource_id UUID,
    action VARCHAR(50),
    result VARCHAR(50),
    ip_address INET,
    user_agent TEXT,
    session_id UUID,
    request_id UUID,
    details JSONB DEFAULT '{}',
    metadata JSONB DEFAULT '{}',
    occurred_at TIMESTAMP WITH TIME ZONE NOT NULL,
    PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

-- Indexes on parent table (inherited by partitions)
CREATE INDEX idx_audit_events_occurred_at		ON authz.audit_events(occurred_at DESC);
CREATE INDEX idx_audit_events_user_id			ON authz.audit_events(user_id, occurred_at DESC);
CREATE INDEX idx_audit_events_org_id			ON authz.audit_events(organization_id, occurred_at DESC);
CREATE INDEX idx_audit_events_type				ON authz.audit_events(event_type, occurred_at DESC);
CREATE INDEX idx_audit_events_category			ON authz.audit_events(event_category, occurred_at DESC);
CREATE INDEX idx_audit_events_result			ON authz.audit_events(result, occurred_at DESC);
CREATE INDEX idx_audit_events_ip				ON authz.audit_events(ip_address);
CREATE INDEX idx_audit_events_resource			ON authz.audit_events(resource_type, resource_id);
CREATE INDEX idx_audit_events_session			ON authz.audit_events(session_id);
CREATE INDEX idx_audit_events_request			ON authz.audit_events(request_id);
CREATE INDEX idx_audit_events_details			ON authz.audit_events USING GIN(details);

-- Create initial partitions (adjust dates as needed)
CREATE TABLE authz.audit_events_y2024m01 PARTITION OF authz.audit_events
	FOR VALUES FROM ('2024-01-01') TO ('2024-02-01');
	
CREATE TABLE authz.audit_events_y2024m02 PARTITION OF authz.audit_events
	FOR VALUES FROM ('2024-02-01') TO ('2024-03-01');
	
CREATE TABLE authz.audit_events_y2024m03 PARTITION OF authz.audit_events
	FOR VALUES FROM ('2024-03-01') TO ('2024-04-01');

-- Comments
COMMENT ON TABLE authz.audit_events IS 'Immutable audit log of all system activities - partitioned by month';
COMMENT ON COLUMN authz.audit_events.id IS 'Unique event identifier';
COMMENT ON COLUMN authz.audit_events.event_type IS 'Specific event type (e.g., PERMISSION_CHECK, ROLE_ASSIGNED)';
COMMENT ON COLUMN authz.audit_events.event_category IS 'Event category (e.g., AUTHORIZATION, AUTHENTICATION, ADMINISTRATION)';
COMMENT ON COLUMN authz.audit_events.user_id IS 'User who triggered the event';
COMMENT ON COLUMN authz.audit_events.organization_id IS 'Organization context for the event';
COMMENT ON COLUMN authz.audit_events.resource_type IS 'Type of resource affected (e.g., USER, ROLE, PERMISSION)';
COMMENT ON COLUMN authz.audit_events.resource_id IS 'ID of the affected resource';
COMMENT ON COLUMN authz.audit_events.action IS 'Action performed (e.g., CREATE, READ, UPDATE, DELETE)';
COMMENT ON COLUMN authz.audit_events.result IS 'Outcome of the action';
COMMENT ON COLUMN authz.audit_events.ip_address IS 'Client IP address';
COMMENT ON COLUMN authz.audit_events.user_agent IS 'Client user agent string';
COMMENT ON COLUMN authz.audit_events.session_id IS 'Session identifier for correlation';
COMMENT ON COLUMN authz.audit_events.request_id IS 'Request identifier for tracing';
COMMENT ON COLUMN authz.audit_events.details IS 'Event-specific details';
COMMENT ON COLUMN authz.audit_events.metadata IS 'Additional metadata';
COMMENT ON COLUMN authz.audit_events.occurred_at IS 'When the event occurred';

-- Function to automatically create monthly partitions
CREATE OR REPLACE FUNCTION authz.create_monthly_partition()
RETURNS void AS $$
DECLARE
	partition_date		DATE;
	partition_name		TEXT;
	start_date			DATE;
	end_date			DATE;
BEGIN
	-- Get next month's date
	partition_date := DATE_TRUNC('month', CURRENT_DATE + INTERVAL '1 month');
	partition_name := 'audit_events_y' || TO_CHAR(partition_date, 'YYYY') || 'm' || TO_CHAR(partition_date, 'MM');
	start_date := partition_date;
	end_date := partition_date + INTERVAL '1 month';
	
	-- Check if partition already exists
	IF NOT EXISTS (
		SELECT 1 
		FROM pg_tables 
		WHERE schemaname = 'authz' 
		AND tablename = partition_name
	) THEN
		-- Create the partition
		EXECUTE format('CREATE TABLE authz.%I PARTITION OF authz.audit_events FOR VALUES FROM (%L) TO (%L)',
			partition_name, start_date, end_date);
		
		RAISE NOTICE 'Created partition: %', partition_name;
	END IF;
END;
$$ LANGUAGE plpgsql;
