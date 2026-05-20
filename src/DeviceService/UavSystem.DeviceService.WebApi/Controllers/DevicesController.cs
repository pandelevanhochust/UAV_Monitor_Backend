using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Application.Features.Devices.Queries;

namespace UavSystem.DeviceService.WebApi.Controllers;

[ApiController]
[Route("api/v1/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly ISender _sender;

    public DevicesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// GET /api/v1/devices — Returns devices visible to the authenticated user.
    /// ADMIN sees all devices; MONITOR sees only assigned devices.
    /// X-User-ID and X-User-Role are injected by Kong ForwardAuth.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDevices(
        [FromHeader(Name = "X-User-ID")] string? userId,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        Guid? monitorId = null;

        // MONITOR users can only see their assigned devices
        if (string.Equals(userRole, "Monitor", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(userId, out var parsedId))
        {
            monitorId = parsedId;
        }

        var devices = await _sender.Send(new GetDevicesQuery(monitorId), ct);
        return Ok(devices);
    }
}
