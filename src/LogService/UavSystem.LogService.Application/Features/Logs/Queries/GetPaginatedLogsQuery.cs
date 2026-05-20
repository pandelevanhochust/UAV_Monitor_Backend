using MediatR;
using UavSystem.LogService.Application.DTOs;
using UavSystem.LogService.Application.Interfaces;

namespace UavSystem.LogService.Application.Features.Logs.Queries;

/// <summary>
/// MediatR query for paginated log retrieval from ClickHouse.
/// Supports optional device ID filter and date range.
/// Device scoping (MONITOR role restriction) is handled at the Controller layer
/// before dispatching this query — this handler always receives pre-validated params.
/// </summary>
public sealed record GetPaginatedLogsQuery(
    long? DeviceId,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50
) : IRequest<PaginatedLogsDto>;

public sealed class GetPaginatedLogsQueryHandler
    : IRequestHandler<GetPaginatedLogsQuery, PaginatedLogsDto>
{
    private readonly ILogRepository _logRepository;

    public GetPaginatedLogsQueryHandler(ILogRepository logRepository)
    {
        _logRepository = logRepository;
    }

    public async Task<PaginatedLogsDto> Handle(
        GetPaginatedLogsQuery request,
        CancellationToken cancellationToken)
    {
        // Clamp pagination values
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        return await _logRepository.GetPaginatedLogsAsync(
            request.DeviceId,
            request.From,
            request.To,
            page,
            pageSize,
            cancellationToken);
    }
}

/// <summary>
/// Scoped query variant — only returns logs for specified device IDs.
/// Used when MONITOR role restriction is active.
/// </summary>
public sealed record GetScopedPaginatedLogsQuery(
    IReadOnlyList<long> AllowedDeviceIds,
    long? DeviceIdFilter,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50
) : IRequest<PaginatedLogsDto>;

public sealed class GetScopedPaginatedLogsQueryHandler
    : IRequestHandler<GetScopedPaginatedLogsQuery, PaginatedLogsDto>
{
    private readonly ILogRepository _logRepository;

    public GetScopedPaginatedLogsQueryHandler(ILogRepository logRepository)
    {
        _logRepository = logRepository;
    }

    public async Task<PaginatedLogsDto> Handle(
        GetScopedPaginatedLogsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        return await _logRepository.GetPaginatedLogsByDeviceIdsAsync(
            request.AllowedDeviceIds,
            request.DeviceIdFilter,
            request.From,
            request.To,
            page,
            pageSize,
            cancellationToken);
    }
}
