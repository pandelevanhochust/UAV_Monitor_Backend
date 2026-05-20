using MediatR;
using Microsoft.AspNetCore.Mvc;
using UavSystem.LogService.Application.DTOs;
using UavSystem.LogService.Application.Features.Logs.Queries;
using UavSystem.Shared.Contracts.Grpc;

namespace UavSystem.LogService.WebApi.Controllers;

[ApiController]
[Route("api/v1/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly UserService.UserServiceClient _userGrpcClient;
    private readonly ILogger<LogsController> _logger;

    public LogsController(
        ISender sender,
        UserService.UserServiceClient userGrpcClient,
        ILogger<LogsController> logger)
    {
        _sender = sender;
        _userGrpcClient = userGrpcClient;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/logs — Returns paginated radar logs from ClickHouse.
    ///
    /// Authorization scoping:
    ///   - ADMIN: full access to all device logs.
    ///   - MONITOR: restricted to assigned devices only via gRPC call
    ///     to UserService.GetUserMonitoredDevices. If the requested device_id
    ///     is not in the monitor's assigned list, returns 403 Forbidden.
    ///
    /// X-User-ID and X-User-Role headers are injected by Kong ForwardAuth.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedLogsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLogs(
        [FromHeader(Name = "X-User-ID")] string? userId,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        [FromQuery] long? device_id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // ── ADMIN: unrestricted access ───────────────────────────────────
        if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _sender.Send(
                new GetPaginatedLogsQuery(device_id, from, to, page, pageSize), ct);
            return Ok(result);
        }

        // ── MONITOR: device-scoped access via gRPC ───────────────────────
        if (string.Equals(userRole, "Monitor", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            // Call UserService gRPC to get assigned device IDs for this monitor
            var grpcRequest = new GetDevicesRequest { UserId = userId };
            var grpcResponse = await _userGrpcClient.GetUserMonitoredDevicesAsync(
                grpcRequest, cancellationToken: ct);

            var allowedDeviceIds = grpcResponse.DeviceIds.ToList();

            if (allowedDeviceIds.Count == 0)
            {
                _logger.LogDebug("Monitor {UserId} has no assigned devices", userId);
                return Ok(new PaginatedLogsDto(
                    Array.Empty<LogEntryDto>(), page, pageSize, 0));
            }

            // If a specific device is requested, verify it's in the allowed list
            if (device_id.HasValue && !allowedDeviceIds.Contains(device_id.Value))
            {
                _logger.LogWarning(
                    "Monitor {UserId} attempted to access device {DeviceId} — FORBIDDEN",
                    userId, device_id.Value);
                return Forbid();
            }

            var result = await _sender.Send(
                new GetScopedPaginatedLogsQuery(
                    allowedDeviceIds, device_id, from, to, page, pageSize), ct);

            return Ok(result);
        }

        // Unknown role
        return Forbid();
    }
}
