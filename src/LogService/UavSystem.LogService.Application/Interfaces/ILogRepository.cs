using UavSystem.LogService.Application.DTOs;

namespace UavSystem.LogService.Application.Interfaces;

/// <summary>
/// Log repository contract for ClickHouse reads.
/// Defined in Application, implemented in Infrastructure.
/// NEVER touches PostgreSQL (Zero Cross-Over policy).
/// </summary>
public interface ILogRepository
{
    /// <summary>
    /// Returns paginated log entries from ClickHouse radar_logs table,
    /// optionally filtered by device ID and date range.
    /// </summary>
    Task<PaginatedLogsDto> GetPaginatedLogsAsync(
        long? deviceId,
        DateTime? from,
        DateTime? to,
        bool? detected,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns paginated log entries filtered to only allowed device IDs
    /// (for MONITOR role scoping).
    /// </summary>
    Task<PaginatedLogsDto> GetPaginatedLogsByDeviceIdsAsync(
        IReadOnlyList<long> deviceIds,
        long? deviceIdFilter,
        DateTime? from,
        DateTime? to,
        bool? detected,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
