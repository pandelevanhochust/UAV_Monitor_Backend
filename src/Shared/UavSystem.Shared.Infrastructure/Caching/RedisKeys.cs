namespace UavSystem.Shared.Infrastructure.Caching;

/// <summary>
/// Centralized Redis key format constants following the snake_case:colon_separated convention.
/// Every service must use these methods — never construct Redis keys via string concatenation.
/// </summary>
public static class RedisKeys
{
    /// <summary>Hash: status, monitor_id, location, api_key_hash. TTL: None (persistent).</summary>
    public static string DeviceMeta(long deviceId)      => $"device:meta:{deviceId}";

    /// <summary>String: "active". TTL: 10 minutes. Expiry triggers OFFLINE transition.</summary>
    public static string DeviceHeartbeat(long deviceId) => $"device:heartbeat:{deviceId}";

    /// <summary>Hash: timestamp, detected, drone_type, accuracy, controlState. Overwritten on each packet.</summary>
    public static string DeviceLatestLog(long deviceId) => $"device:latest_log:{deviceId}";

    /// <summary>Set: [device_id, ...]. TTL: None (persistent). Monitor → devices index.</summary>
    public static string MonitorDevices(Guid userId)    => $"monitor:devices:{userId}";
}
