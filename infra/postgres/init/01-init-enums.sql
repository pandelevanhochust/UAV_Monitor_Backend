-- ============================================================================
-- UAV Drone Detection System — PostgreSQL Cluster Initialization
-- Phase 1: Enable shared extensions on the default 'postgres' database.
--
-- REMOVED: CREATE TYPE ... AS ENUM (user_role, device_status)
-- REASON:  EF Core is configured with .HasConversion<string>() in both
--          UserDbContext and DeviceDbContext. Physical enum types in PostgreSQL
--          cause migration conflicts when multiple services share one cluster
--          and make future schema evolution unnecessarily difficult.
--          Values are stored as VARCHAR/TEXT — simpler, portable, extensible.
--
-- Execution Order: 01 (runs FIRST, in the default 'postgres' DB context)
-- ============================================================================

-- Enable UUID generation (available cluster-wide after creation)
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
