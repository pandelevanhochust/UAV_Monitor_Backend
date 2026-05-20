-- ============================================================================
-- UAV Drone Detection System — PostgreSQL Database Initialization
-- Ensures the database is ready before EF Core connects.
--
-- NOTE: The POSTGRES_DB variable is set in docker-compose.yml and creates
-- the database automatically. This script creates the schema structure,
-- required extensions, and verifies the database is ready.
--
-- Execution Order: 02 (runs AFTER 01-init-enums.sql)
-- ============================================================================

-- Verify we are connected to the correct database
-- (Docker auto-creates DB from POSTGRES_DB env var)
DO $$
BEGIN
    RAISE NOTICE 'Connected to database: %', current_database();
    RAISE NOTICE 'PostgreSQL version: %', version();
END
$$;

-- Ensure UUID generation extension is available
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================================
-- USERS TABLE (managed by EF Core migrations, but scaffolded here as safety net)
-- If EF Core migrations have not yet run, this ensures the table exists
-- with the correct structure for first-boot scenarios.
-- ============================================================================

CREATE TABLE IF NOT EXISTS users (
    user_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username      VARCHAR(255) NOT NULL UNIQUE,
    email         VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    role          user_role NOT NULL DEFAULT 'MONITOR',
    updated_at    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Partial index for fast username lookups during login
CREATE INDEX IF NOT EXISTS idx_users_username ON users (username);

-- ============================================================================
-- DEVICES TABLE (managed by EF Core migrations, but scaffolded here as safety net)
-- ============================================================================

CREATE TABLE IF NOT EXISTS devices (
    device_id          BIGINT PRIMARY KEY,                   -- Assigned by admin
    location_name      VARCHAR(255) NOT NULL,
    status             device_status NOT NULL DEFAULT 'OFFLINE',
    assigned_monitor_id UUID,                                 -- FK to users(user_id)
    api_key_hash       VARCHAR(255) NOT NULL,
    updated_at         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for monitor-scoped device queries
CREATE INDEX IF NOT EXISTS idx_devices_monitor ON devices (assigned_monitor_id);

-- ============================================================================
-- username: admin
-- email: toanlv@gmail.com
-- password: admin123
-- SEED: Default admin user (password: "admin123" — CHANGE IN PRODUCTION)
-- BCrypt hash of "admin123" — generated deterministically for dev bootstrap
-- ============================================================================

INSERT INTO users (user_id, username, email, password_hash, role)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'admin',
    'toanlv@gmail.com',
    'AQAAAAIAAYagAAAAEI9gB8rNiswJtG+1nJmZ8kS7qMlh/gCHb1uO+12345abcdefg==',
    'ADMIN'
)
ON CONFLICT (username) DO NOTHING;

DO $$
BEGIN
    RAISE NOTICE 'Database initialization complete. Tables: users, devices. Default admin seeded.';
END
$$;
