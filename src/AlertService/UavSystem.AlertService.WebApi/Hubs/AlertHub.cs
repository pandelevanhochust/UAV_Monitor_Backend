using Microsoft.AspNetCore.SignalR;

namespace UavSystem.AlertService.WebApi.Hubs;

/// <summary>
/// SignalR hub for real-time alert delivery to connected supervisors/monitors.
///
/// Clients connect to: ws://host/ws/alerts?userId={userId}
///
/// Group isolation strategy:
///   - Each client is mapped into a SignalR group keyed by their userId.
///   - When a DroneDetectedEvent or DeviceStatusChangedEvent fires,
///     the consumer performs a Redis reverse-lookup to resolve which
///     userId is the assigned monitor for the source device_id, then
///     dispatches the alert to that specific group only.
///
/// This ensures monitors ONLY receive alerts for their assigned devices.
/// </summary>
public sealed class AlertHub : Hub
{
    private readonly ILogger<AlertHub> _logger;

    public AlertHub(ILogger<AlertHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Extract userId from query string: ws://host/ws/alerts?userId=<guid>
        var httpContext = Context.GetHttpContext();
        var userId = httpContext?.Request.Query["userId"].ToString();

        if (string.IsNullOrWhiteSpace(userId))
        {
            // Fallback: try to extract from JWT claims if present
            userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Client {ConnectionId} connected without userId — rejecting", Context.ConnectionId);
            Context.Abort();
            return;
        }

        // Map client into isolation partition by userId
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);

        _logger.LogInformation(
            "Client {ConnectionId} joined group '{UserId}' — listening for alerts",
            Context.ConnectionId, userId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var httpContext = Context.GetHttpContext();
        var userId = httpContext?.Request.Query["userId"].ToString()
                     ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);

            _logger.LogInformation(
                "Client {ConnectionId} left group '{UserId}'",
                Context.ConnectionId, userId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
