-- ============================================================================
-- UAV Drone Detection System — ClickHouse Schema Initialization
-- Table: radar_logs (columnar time-series store for RF telemetry)
--
-- Written by: IngestionService (ClickHouseBulkCopy batch insert)
-- Read by:    LogService (parameterized SELECT queries)
--
-- Column names use strict snake_case to match the .NET data pipeline:
--   IngestionWorker.WriteToClickHouseAsync() → BulkCopy column order
--   ClickHouseLogRepository.GetPageAsync()   → SELECT column order
-- ============================================================================

CREATE DATABASE IF NOT EXISTS uav_logs;

CREATE TABLE IF NOT EXISTS uav_logs.radar_logs
(
    device_id      UInt16,
    timestamp      DateTime64(3, 'UTC'),
    status         LowCardinality(String),
    detected       Bool,                              
    drone_type     LowCardinality(String),
    accuracy       Float32,
    control_state  LowCardinality(Nullable(String)),
    latency        Float32,
    frequency      Float32
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (device_id, timestamp)
TTL toDateTime(timestamp) + INTERVAL 3 MONTH;
