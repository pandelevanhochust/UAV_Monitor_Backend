using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using UavSystem.DeviceService.Application.Features.Devices.Commands;
using UavSystem.DeviceService.Infrastructure.Messaging;
using UavSystem.Shared.Contracts.Grpc;

namespace UavSystem.DeviceService.WebApi.GrpcServices;

/// <summary>
/// gRPC server implementing InternalDeviceService.InternalDeviceServiceBase.
/// Listens on port :9081 over HTTP/2 for internal calls from IngestionService.
/// </summary>
public sealed class DeviceGrpcService : InternalDeviceService.InternalDeviceServiceBase
{
    private readonly ISender _sender;
    private readonly DeviceStatusPublisher _statusPublisher;
    private readonly ILogger<DeviceGrpcService> _logger;

    public DeviceGrpcService(
        ISender sender,
        DeviceStatusPublisher statusPublisher,
        ILogger<DeviceGrpcService> logger)
    {
        _sender = sender;
        _statusPublisher = statusPublisher;
        _logger = logger;
    }

    public override async Task<UpdateDeviceStatusResponse> UpdateDeviceStatus(
        UpdateDeviceStatusRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug("gRPC UpdateDeviceStatus: device={DeviceId}, status={Status}",
            request.DeviceId, request.NewStatus);

        var success = await _sender.Send(
            new UpdateDeviceStatusCommand(request.DeviceId, request.NewStatus),
            context.CancellationToken);

        return new UpdateDeviceStatusResponse { Success = success };
    }

    public override async Task<StateChangeResponse> ReportStateChange(
        StateChangeRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "gRPC ReportStateChange: device={DeviceId}, {OriginalStatus}→{UpdatedStatus}",
            request.DeviceId, request.OriginalStatus, request.UpdatedStatus);

        // Update PostgreSQL + Redis via MediatR
        var dbCommitted = await _sender.Send(
            new UpdateDeviceStatusCommand(request.DeviceId, request.UpdatedStatus),
            context.CancellationToken);

        var eventBroadcasted = false;

        if (dbCommitted)
        {
            try
            {
                await _statusPublisher.PublishStatusChangedAsync(
                    request.DeviceId,
                    request.OriginalStatus,
                    request.UpdatedStatus,
                    context.CancellationToken);

                eventBroadcasted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish status change event for device {DeviceId}",
                    request.DeviceId);
            }
        }

        return new StateChangeResponse
        {
            DatabaseCommitted = dbCommitted,
            EventBroadcasted = eventBroadcasted
        };
    }
}
