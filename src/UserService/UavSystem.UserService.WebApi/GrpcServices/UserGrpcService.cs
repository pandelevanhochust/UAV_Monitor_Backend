using Grpc.Core;
using MediatR;
using UavSystem.Shared.Contracts.Grpc;
using UavSystem.UserService.Application.Features.Auth.Queries;

namespace UavSystem.UserService.WebApi.GrpcServices;

/// <summary>
/// gRPC server implementing UserService.UserServiceBase from the shared proto.
/// Serves Kong ForwardAuth JWT validation requests on port :9080 over HTTP/2.
/// Also serves GetUserMonitoredDevices for LogService monitor scoping.
/// </summary>
public sealed class UserGrpcService : UavSystem.Shared.Contracts.Grpc.UserService.UserServiceBase
{
    private readonly ISender _sender;
    private readonly ILogger<UserGrpcService> _logger;

    public UserGrpcService(ISender sender, ILogger<UserGrpcService> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public override async Task<ValidateTokenResponse> ValidateToken(
        ValidateTokenRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug("gRPC ValidateToken called from {Peer}", context.Peer);

        var result = await _sender.Send(
            new ValidateTokenQuery(request.Token),
            context.CancellationToken);

        return new ValidateTokenResponse
        {
            UserId = result.UserId,
            Role = result.Role,
            IsValid = result.IsValid
        };
    }

    public override Task<GetDevicesResponse> GetUserMonitoredDevices(
        GetDevicesRequest request,
        ServerCallContext context)
    {
        // TODO: Phase 4+ — query device assignments for the given user.
        // For now return empty — will be wired when DeviceService is integrated.
        _logger.LogDebug("gRPC GetUserMonitoredDevices called for user {UserId}", request.UserId);

        return Task.FromResult(new GetDevicesResponse());
    }
}
