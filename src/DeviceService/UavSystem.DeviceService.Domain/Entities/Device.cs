using UavSystem.DeviceService.Domain.Enums;

namespace UavSystem.DeviceService.Domain.Entities;

/// <summary>
/// Core domain entity representing a physical radar device (SDR edge hardware).
/// Immutable where possible — uses private set / init accessors.
/// </summary>
public sealed class Device
{
    public long DeviceId { get; init; }
    public string LocationName { get; private set; } = null!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Offline;
    public Guid? AssignedMonitorId { get; private set; }
    public string ApiKeyHash { get; private set; } = null!;  // BCrypt hash
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    // Required by EF Core for materialization — private
    private Device() { }

    public Device(long deviceId, string locationName, string apiKeyHash)
    {
        DeviceId = deviceId;
        LocationName = locationName ?? throw new ArgumentNullException(nameof(locationName));
        ApiKeyHash = apiKeyHash ?? throw new ArgumentNullException(nameof(apiKeyHash));
    }

    public void UpdateStatus(DeviceStatus newStatus)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AssignMonitor(Guid? monitorId)
    {
        AssignedMonitorId = monitorId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLocation(string locationName)
    {
        LocationName = locationName ?? throw new ArgumentNullException(nameof(locationName));
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateApiKeyHash(string apiKeyHash)
    {
        ApiKeyHash = apiKeyHash ?? throw new ArgumentNullException(nameof(apiKeyHash));
        UpdatedAt = DateTime.UtcNow;
    }
}
