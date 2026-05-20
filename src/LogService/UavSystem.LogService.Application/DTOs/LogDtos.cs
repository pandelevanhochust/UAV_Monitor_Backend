namespace UavSystem.LogService.Application.DTOs;

/// <summary>
/// Read-only DTO returned from ClickHouse log queries.
/// Matches the radar_logs table schema exactly.
/// </summary>
public sealed record LogEntryDto(
    long DeviceId,
    DateTime Timestamp,
    string Status,
    bool Detected,
    string DroneType,
    float Accuracy,
    string? ControlState
);

/// <summary>
/// Paginated response envelope for log queries.
/// </summary>
public sealed record PaginatedLogsDto(
    IReadOnlyList<LogEntryDto> Items,
    int Page,
    int PageSize,
    long TotalCount
);
