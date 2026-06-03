using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.DeviceService.Application.DTOs;
using UavSystem.DeviceService.Application.Features.Devices.Commands;
using UavSystem.DeviceService.Application.Features.Devices.Queries;

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
    /// GET /api/v1/admin/devices — Returns all devices (ADMIN only).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllDevices(
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "Admin role required." });

        var devices = await _sender.Send(new GetDevicesQuery(null), ct);
        return Ok(devices);
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
            return StatusCode(403, new { message = "Admin role required." });

        var command = new RegisterDeviceCommand(
            request.DeviceId,
            request.LocationName,
            request.AssignedMonitorId);

        var result = await _sender.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// POST /api/v1/admin/devices/{deviceId}/assign-monitor — Assigns or unassigns
    /// a monitor operator to an existing device (ADMIN only).
    /// Send { "monitorId": null } to unassign.
    /// </summary>
    [HttpPost("{deviceId}/assign-monitor")]
    [ProducesResponseType(typeof(DeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignMonitor(
        long deviceId,
        [FromBody] AssignMonitorRequestDto request,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        CancellationToken ct)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "Admin role required." });

        var command = new AssignMonitorCommand(deviceId, request.MonitorId);
        var result = await _sender.Send(command, ct);
        return Ok(result);
    }
}
