using MediatR;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Application.Features.Devices.Commands;

/// <summary>
/// Assigns or unassigns a monitor operator to an existing radar device.
/// Persists to PostgreSQL and syncs to Redis Write-Through.
/// MonitorId = null → unassign the device.
/// </summary>
public sealed record AssignMonitorCommand(
    long DeviceId,
    Guid? MonitorId
) : IRequest<DeviceDto>;

public sealed class AssignMonitorCommandHandler
    : IRequestHandler<AssignMonitorCommand, DeviceDto>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IRedisDeviceSyncService _redisSyncService;

    public AssignMonitorCommandHandler(
        IDeviceRepository deviceRepository,
        IRedisDeviceSyncService redisSyncService)
    {
        _deviceRepository = deviceRepository;
        _redisSyncService = redisSyncService;
    }

    public async Task<DeviceDto> Handle(
        AssignMonitorCommand request,
        CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(request.DeviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Device {request.DeviceId} not found.");

        // Domain method handles nullability (assign or unassign)
        device.AssignMonitor(request.MonitorId);

        await _deviceRepository.UpdateAsync(device, cancellationToken);
        await _deviceRepository.SaveChangesAsync(cancellationToken);

        // Write-Through: sync updated monitor assignment to Redis
        await _redisSyncService.SyncDeviceMetadataAsync(
            device.DeviceId,
            device.Status.ToString(),
            device.AssignedMonitorId,
            device.LocationName,
            device.ApiKeyHash,
            cancellationToken);

        if (request.MonitorId.HasValue)
        {
            await _redisSyncService.SyncMonitorDevicesAsync(
                request.MonitorId.Value,
                device.DeviceId,
                cancellationToken);
        }

        return new DeviceDto(
            device.DeviceId,
            device.LocationName,
            device.Status.ToString(),
            device.AssignedMonitorId,
            device.UpdatedAt);
    }
}
