-- =============================================================================
-- STEP 1: Core Logical Database Creation
-- =============================================================================
SELECT 'CREATE DATABASE uav_user_db' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'uav_user_db')\gexec
SELECT 'CREATE DATABASE uav_device_db' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'uav_device_db')\gexec

-- =============================================================================
-- STEP 2: Initialize uav_user_db Schema and Seed Data
-- =============================================================================
\c uav_user_db

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY,
    username VARCHAR(50) NOT NULL,
    email VARCHAR(150) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    role VARCHAR(50) NOT NULL, -- Stored as plain text string!
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO users (id, username, email, password_hash, role, updated_at)
VALUES
    ('1', 'admin', 'toanlv@gmail.com', '$2a$11$kr2nwn5997f5u8JxORm.QePqd91TxqzaVDBWPMU9zMV59xwhng0iq', 'Admin', NOW()),
    ('2', 'monitor_toan', 'toan.monitor@uav-system.local', '$2a$11$97d9Vcgd7jd8RBJ52l8Al.YkRiHzxmDvR05.TkF9DaTz16pU3uS0i', 'Monitor', NOW()),
    ('3', 'monitor_hung', 'hung.monitor@uav-system.local', '$2a$11$1ShpIx7rv/I9IGDP8mxE/eCNRsanYsnMzXv/xyT8JZ2a0fGM86WhO', 'Monitor', NOW())
ON CONFLICT (id) DO NOTHING;

-- Create EF Core migration tracking table manually if it doesn't exist so seed works
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32) NOT NULL
);

-- =============================================================================
-- STEP 3: Initialize uav_device_db Schema and Seed Data
-- =============================================================================
\c uav_device_db

CREATE TABLE IF NOT EXISTS devices (
    device_id BIGINT PRIMARY KEY,
    location_name VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL, -- Stored as plain text string!
    assigned_monitor_id UUID NULL,
    api_key_hash VARCHAR(255) NOT NULL,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO devices (device_id, location_name, status, assigned_monitor_id, api_key_hash, updated_at)
VALUES
    (1001, 'Hanoi - North Gate Radar Station', 'Offline', '00000000-0000-0000-0000-000000000002', '$2a$11$fDX.YSw.UtCLsF.6cwD/MO2Pg4JiiWsCBIhay28ToisFyJkVrPoW6', NOW()),
    (1002, 'Hanoi - East Perimeter Radar Station', 'Offline', '00000000-0000-0000-0000-000000000003', '$2a$11$QYD3/P1xDKONuNMft9mfvu0EaPGIVOqTf8pL3lWU.sBArvKTE9Nu6', NOW()),
    (1003, 'Hanoi - South Airbase Radar Station', 'Offline', NULL, '$2a$11$5Lw27ACIxHAal2mtESZEl.DUnDy.IqT6QjcwEGuIL9XWgJNxWo7.K', NOW())
ON CONFLICT (device_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" varchar(150) NOT NULL PRIMARY KEY,
    "ProductVersion" varchar(32) NOT NULL
);