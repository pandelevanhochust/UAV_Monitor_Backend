-- ============================================================================
-- UAV Drone Detection System — PostgreSQL Initialization
-- Creates enum types and extensions required by UserService & DeviceService
-- ============================================================================

-- Enable UUID generation
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Enum types used by EF Core mappings
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'user_role') THEN
        CREATE TYPE user_role AS ENUM ('ADMIN', 'MONITOR');
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'device_status') THEN
        CREATE TYPE device_status AS ENUM ('ONLINE', 'OFFLINE', 'ERROR');
    END IF;
END
$$;
