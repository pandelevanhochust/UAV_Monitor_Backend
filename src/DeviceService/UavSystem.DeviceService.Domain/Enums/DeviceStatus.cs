namespace UavSystem.DeviceService.Domain.Enums;

/// <summary>
/// Operational state of a physical radar device.
/// Domain-owned enum — zero external dependencies.
/// </summary>
public enum DeviceStatus
{
    Online,
    Offline,
    Error
}
