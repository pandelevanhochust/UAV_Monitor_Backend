namespace UavSystem.Shared.Contracts.Enums;

/// <summary>
/// Defines the two authorization roles enforced across the UAV system.
/// ADMIN: Full CRUD on users and devices, assign monitors, view all logs.
/// MONITOR: Read-only; view only logs and device status for assigned devices.
/// </summary>
public enum UserRole
{
    Admin,
    Monitor
}
