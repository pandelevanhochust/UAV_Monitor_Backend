using MediatR;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Application.Features.Devices.Commands;

/// <summary>
/// Updates a device's operational status. Called internally by gRPC
/// (from IngestionService state diff or heartbeat expiry watcher).
/// Writes to PostgreSQL AND Redis (Write-Through).
/// </summary>
public sealed record UpdateDeviceStatusCommand(
    long DeviceId,
    string NewStatus
) : IRequest<bool>;

public sealed class UpdateDeviceStatusCommandHandler
    : IRequestHandler<UpdateDeviceStatusCommand, bool>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IRedisDeviceSyncService _redisSyncService;

    public UpdateDeviceStatusCommandHandler(
        IDeviceRepository deviceRepository,
        IRedisDeviceSyncService redisSyncService)
    {
        _deviceRepository = deviceRepository;
        _redisSyncService = redisSyncService;
    }

    public async Task<bool> Handle(UpdateDeviceStatusCommand request, CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return false;

        var newStatus = Enum.Parse<Domain.Enums.DeviceStatus>(request.NewStatus, ignoreCase: true);
        device.UpdateStatus(newStatus);

        await _deviceRepository.UpdateAsync(device, cancellationToken);
        await _deviceRepository.SaveChangesAsync(cancellationToken);

        // Write-Through: sync status to Redis immediately (Appendix C Rule 7)
        await _redisSyncService.UpdateDeviceStatusAsync(
            request.DeviceId,
            newStatus.ToString(),
            cancellationToken);

        return true;
    }
}
