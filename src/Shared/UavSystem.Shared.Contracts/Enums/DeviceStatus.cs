namespace UavSystem.Shared.Contracts.Enums;

/// <summary>
/// Represents the operational state of a physical radar device.
/// Transitions: ONLINE ↔ OFFLINE (via heartbeat expiry), ONLINE → ERROR (via edge report).
/// Stored in PostgreSQL (DeviceService) and cached in Redis device:meta:{id}.
/// </summary>
public enum DeviceStatus
{
    Online,
    Offline,
    Error
}
