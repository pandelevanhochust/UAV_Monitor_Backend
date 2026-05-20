using System.Security.Cryptography;
using MediatR;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Application.Interfaces;
using UavSystem.DeviceService.Domain.Entities;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Application.Features.Devices.Commands;

/// <summary>
/// Registers a new radar device. Generates an API key, hashes it,
/// stores in PostgreSQL AND syncs to Redis (Write-Through).
/// Returns the raw API key ONCE — it is never stored or retrievable again.
/// </summary>
public sealed record RegisterDeviceCommand(
    long DeviceId,
    string LocationName,
    Guid? AssignedMonitorId
) : IRequest<RegisterDeviceResponseDto>;

public sealed class RegisterDeviceCommandHandler
    : IRequestHandler<RegisterDeviceCommand, RegisterDeviceResponseDto>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IRedisDeviceSyncService _redisSyncService;

    public RegisterDeviceCommandHandler(
        IDeviceRepository deviceRepository,
        IRedisDeviceSyncService redisSyncService)
    {
        _deviceRepository = deviceRepository;
        _redisSyncService = redisSyncService;
    }

    public async Task<RegisterDeviceResponseDto> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        // Generate a cryptographically secure API key
        var rawApiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var apiKeyHash = BCrypt.Net.BCrypt.HashPassword(rawApiKey);

        var device = new Device(request.DeviceId, request.LocationName, apiKeyHash);

        if (request.AssignedMonitorId.HasValue)
            device.AssignMonitor(request.AssignedMonitorId.Value);

        await _deviceRepository.AddAsync(device, cancellationToken);
        await _deviceRepository.SaveChangesAsync(cancellationToken);

        // Write-Through: sync to Redis immediately after PostgreSQL commit (Appendix C Rule 7)
        await _redisSyncService.SyncDeviceMetadataAsync(
            device.DeviceId,
            device.Status.ToString(),
            device.AssignedMonitorId,
            device.LocationName,
            apiKeyHash,
            cancellationToken);

        if (request.AssignedMonitorId.HasValue)
        {
            await _redisSyncService.SyncMonitorDevicesAsync(
                request.AssignedMonitorId.Value,
                device.DeviceId,
                cancellationToken);
        }

        return new RegisterDeviceResponseDto(
            DeviceId: device.DeviceId,
            ApiKey: rawApiKey,
            LocationName: device.LocationName,
            Status: device.Status.ToString()
        );
    }
}
