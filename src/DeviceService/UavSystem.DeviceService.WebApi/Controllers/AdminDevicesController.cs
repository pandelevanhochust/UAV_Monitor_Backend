using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Application.Features.Devices.Commands;

namespace UavSystem.DeviceService.WebApi.Controllers;

[ApiController]
[Route("api/v1/admin/devices")]
public sealed class AdminDevicesController : ControllerBase
{
    private readonly ISender _sender;

    public AdminDevicesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// POST /api/v1/admin/devices — Registers a new radar device (ADMIN only).
    /// Generates API key, stores hash in PostgreSQL + syncs to Redis.
    /// Returns the raw API key ONCE — it is never retrievable again.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RegisterDeviceResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequestDto request,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var command = new RegisterDeviceCommand(
            request.DeviceId,
            request.LocationName,
            request.AssignedMonitorId);

        var result = await _sender.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
