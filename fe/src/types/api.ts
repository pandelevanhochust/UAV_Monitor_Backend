/**
 * API type definitions for the UAV Drone Detection System frontend.
 *
 * All shapes are derived from the C# entity / DTO definitions in claude.md.
 * ASP.NET Core's default System.Text.Json serializer produces camelCase JSON.
 *
 * Enums: DeviceStatus { Online, Offline, Error } and UserRole { Admin, Monitor }
 * are serialized as PascalCase strings (C# default enum-to-string behavior).
 */

// ─── Enums ────────────────────────────────────────────────────────────────────

export type DeviceStatus = 'Online' | 'Offline' | 'Error';
export type UserRole = 'Admin' | 'Monitor';

// ─── Device ───────────────────────────────────────────────────────────────────

/**
 * Device entity from DeviceService (claude.md §4 — DeviceDbContext)
 * Route: GET /api/v1/devices (monitor-scoped) or /api/v1/admin/devices (all)
 * DeviceService merges Redis device:latest_log:{id} into the response.
 */
export interface Device {
  deviceId: number;
  locationName: string;
  status: DeviceStatus;
  assignedMonitorId: string | null;
  updatedAt: string; // ISO 8601 string
  latestLog: LatestDeviceLog | null;
}

/** Returned by POST /admin/devices/{id}/assign-monitor */
export interface DeviceDto {
  deviceId: number;
  locationName: string;
  status: DeviceStatus;
  assignedMonitorId: string | null;
  updatedAt: string;
}

export interface LatestDeviceLog {
  timestamp: string;
  detected: boolean;
  droneType: string | null;
  controlState: string | null;
  accuracy: number;
}

// ─── Radar Log ────────────────────────────────────────────────────────────────

/**
 * Log record from LogService / ClickHouse radar_logs table (claude.md §4)
 * Route: GET /api/v1/logs?device_id=xxx&start_time=...&end_time=...&limit=50&page=1
 */
export interface RadarLog {
  deviceId: number;
  timestamp: string; // ISO 8601 string — camelCase from C# 'Timestamp'
  status: string;
  detected: boolean;
  droneType: string | null;
  controlState: string | null;
  accuracy: number;
  latency: number;
}

/** Matches C# PaginatedLogsDto(Items, Page, PageSize, TotalCount) */
export interface PaginatedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// ─── Alert / Detection Event ──────────────────────────────────────────────────

/**
 * Drone detection event broadcast by AlertService over SignalR.
 * Event name: "DroneDetected"
 * Hub path: /ws/alerts
 * Source: DroneDetectedEvent record (claude.md §4 — RabbitMQ message format)
 */
export interface DroneDetectionEvent {
  deviceId: number;
  location: string;
  droneType: string;
  controlState: string;
  accuracy: number;
  detectedAt: string; // ISO 8601 string
}

/**
 * Device status change event broadcast by AlertService.
 * Event name: "DeviceStatusChanged"
 */
export interface DeviceStatusChangedEvent {
  deviceId: number;
  location: string;
  previousStatus: DeviceStatus;
  newStatus: DeviceStatus;
  occurredAt: string;
}

// ─── User ─────────────────────────────────────────────────────────────────────

/**
 * User entity from UserService (claude.md §4 — UserDbContext)
 * Route: GET /api/v1/admin/users (Admin only)
 */
export interface AppUser {
  id: string;
  username: string;
  email: string;
  role: UserRole;
  updatedAt: string;
}

// ─── Misc ─────────────────────────────────────────────────────────────────────

/** Generic API error response shape (RFC 7807 Problem Details) */
export interface ApiError {
  title?: string;
  message?: string;
  status?: number;
  detail?: string;
}
