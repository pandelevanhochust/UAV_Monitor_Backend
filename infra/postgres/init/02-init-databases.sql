-- ============================================================================
-- UAV Drone Detection System — Database-per-Service Initialization
--
-- ARCHITECTURE: Each microservice owns its own isolated PostgreSQL database.
--   • UserService   → uav_user_db
--   • DeviceService → uav_device_db
--
-- This script runs automatically when the postgres container starts for the
-- FIRST TIME (Docker mounts it into /docker-entrypoint-initdb.d/).
-- It will NOT re-run on subsequent starts (Docker skips init if data exists).
--
-- Execution Order: 02 (runs AFTER 01-init-enums.sql)
-- Context: Executes in the default 'postgres' superuser session.
-- ============================================================================

-- ── 1. Create isolated databases ─────────────────────────────────────────────

-- UserService owns this database exclusively
CREATE DATABASE uav_user_db
    WITH
    OWNER     = uav_admin
    ENCODING  = 'UTF8'
    LC_COLLATE = 'en_US.utf8'
    LC_CTYPE   = 'en_US.utf8'
    TEMPLATE  = template0;

-- DeviceService owns this database exclusively
CREATE DATABASE uav_device_db
    WITH
    OWNER     = uav_admin
    ENCODING  = 'UTF8'
    LC_COLLATE = 'en_US.utf8'
    LC_CTYPE   = 'en_US.utf8'
    TEMPLATE  = template0;

-- ── 2. Grant privileges ───────────────────────────────────────────────────────

GRANT ALL PRIVILEGES ON DATABASE uav_user_db   TO uav_admin;
GRANT ALL PRIVILEGES ON DATABASE uav_device_db TO uav_admin;

-- ── 3. Enable extensions in each database ────────────────────────────────────
-- Extensions must be created per-database in PostgreSQL.

\connect uav_user_db
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\connect uav_device_db
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ── 4. Confirmation ──────────────────────────────────────────────────────────
\connect postgres

DO $$
BEGIN
    RAISE NOTICE '=======================================================';
    RAISE NOTICE 'Database-per-Service initialization complete.';
    RAISE NOTICE '  uav_user_db   → owned by uav_admin (UserService)';
    RAISE NOTICE '  uav_device_db → owned by uav_admin (DeviceService)';
    RAISE NOTICE 'EF Core migrations will create tables on first run.';
    RAISE NOTICE '=======================================================';
END
$$;
