using MediatR;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Domain.Interfaces;

namespace UavSystem.DeviceService.Application.Features.Devices.Queries;

/// <summary>
/// Retrieves devices — optionally filtered by monitor assignment.
/// </summary>
public sealed record GetDevicesQuery(Guid? MonitorId = null) : IRequest<IEnumerable<DeviceDto>>;

public sealed class GetDevicesQueryHandler
    : IRequestHandler<GetDevicesQuery, IEnumerable<DeviceDto>>
{
    private readonly IDeviceRepository _deviceRepository;

    public GetDevicesQueryHandler(IDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task<IEnumerable<DeviceDto>> Handle(
        GetDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var devices = request.MonitorId.HasValue
            ? await _deviceRepository.GetByMonitorIdAsync(request.MonitorId.Value, cancellationToken)
            : await _deviceRepository.GetAllAsync(cancellationToken);

        return devices.Select(d => new DeviceDto(
            d.DeviceId,
            d.LocationName,
            d.Status.ToString(),
            d.AssignedMonitorId,
            d.UpdatedAt
        ));
    }
}
