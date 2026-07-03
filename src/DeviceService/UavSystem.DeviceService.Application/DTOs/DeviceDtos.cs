namespace UavSystem.DeviceService.Application.DTOs;

public sealed record DeviceDto(
    long DeviceId,
    string LocationName,
    string Status,
    Guid? AssignedMonitorId,
    DateTime UpdatedAt
);

public sealed record RegisterDeviceRequestDto(
    long DeviceId,
    string LocationName,
    Guid? AssignedMonitorId
);

public sealed record RegisterDeviceResponseDto(
    long DeviceId,
    string ApiKey,  // Raw API key returned ONCE at registration
    string LocationName,
    string Status
);

public sealed record AssignMonitorRequestDto(
    Guid? MonitorId  // null = unassign
);
