using System.Data;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Logging;
using UavSystem.LogService.Application.DTOs;
using UavSystem.LogService.Application.Interfaces;

namespace UavSystem.LogService.Infrastructure.ClickHouse;

/// <summary>
/// ClickHouse-based log repository. Uses raw parameterized SQL with the
/// official ClickHouse.Client package. NEVER touches PostgreSQL.
///
/// Query parameters use the explicit {param:Type} formatting syntax
/// as required by ClickHouse.Client's parameterized query support.
/// </summary>
public sealed class ClickHouseLogRepository : ILogRepository
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<ClickHouseLogRepository> _logger;

    public ClickHouseLogRepository(ClickHouseConnection connection, ILogger<ClickHouseLogRepository> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<PaginatedLogsDto> GetPaginatedLogsAsync(
        long? deviceId, DateTime? from, DateTime? to, bool? detected,
        int page, int pageSize, CancellationToken ct = default)
    {
        var (whereClause, parameters) = BuildWhereClause(deviceId, from, to, detected);

        var totalCount = await GetTotalCountAsync(whereClause, parameters, ct);
        var items = await GetPageAsync(whereClause, parameters, page, pageSize, ct);

        return new PaginatedLogsDto(items, page, pageSize, totalCount);
    }

    public async Task<PaginatedLogsDto> GetPaginatedLogsByDeviceIdsAsync(
        IReadOnlyList<long> deviceIds, long? deviceIdFilter,
        DateTime? from, DateTime? to, bool? detected,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (deviceIds.Count == 0)
            return new PaginatedLogsDto(Array.Empty<LogEntryDto>(), page, pageSize, 0);

        // If a specific device is requested, verify it's in the allowed list
        if (deviceIdFilter.HasValue && !deviceIds.Contains(deviceIdFilter.Value))
            return new PaginatedLogsDto(Array.Empty<LogEntryDto>(), page, pageSize, 0);

        var (whereClause, parameters) = BuildScopedWhereClause(
            deviceIds, deviceIdFilter, from, to, detected);

        var totalCount = await GetTotalCountAsync(whereClause, parameters, ct);
        var items = await GetPageAsync(whereClause, parameters, page, pageSize, ct);

        return new PaginatedLogsDto(items, page, pageSize, totalCount);
    }

    private async Task<long> GetTotalCountAsync(
        string whereClause, Dictionary<string, object> parameters, CancellationToken ct)
    {
        var countSql = $"SELECT count() FROM radar_logs {whereClause}";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = countSql;
        AddParameters(cmd, parameters);

        await EnsureOpenAsync(ct);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private async Task<IReadOnlyList<LogEntryDto>> GetPageAsync(
        string whereClause, Dictionary<string, object> parameters,
        int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;

        var dataSql = $@"
            SELECT device_id, timestamp, status, detected, drone_type, accuracy, control_state, latency, frequency
            FROM radar_logs
            {whereClause}
            ORDER BY timestamp DESC
            LIMIT {pageSize} OFFSET {offset}";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = dataSql;
        AddParameters(cmd, parameters);

        await EnsureOpenAsync(ct);
        using var reader = await cmd.ExecuteReaderAsync(ct);

        var items = new List<LogEntryDto>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new LogEntryDto(
                DeviceId: Convert.ToInt64(reader.GetValue(0)),
                Timestamp: reader.GetDateTime(1),
                Status: reader.GetString(2),
                Detected: reader.GetBoolean(3),
                DroneType: reader.GetString(4),
                Accuracy: reader.GetFloat(5),
                ControlState: reader.IsDBNull(6) ? null : reader.GetString(6),
                Latency: reader.GetFloat(7),
                Frequency: reader.GetFloat(8)
            ));
        }

        return items;
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildWhereClause(
        long? deviceId, DateTime? from, DateTime? to, bool? detected)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (deviceId.HasValue)
        {
            conditions.Add("device_id = {device_id:UInt16}");
            parameters["device_id"] = Convert.ToUInt16(deviceId.Value);
        }

        if (from.HasValue)
        {
            conditions.Add("timestamp >= {from_ts:DateTime}");
            parameters["from_ts"] = from.Value;
        }

        if (to.HasValue)
        {
            conditions.Add("timestamp <= {to_ts:DateTime}");
            parameters["to_ts"] = to.Value;
        }

        if (detected.HasValue)
        {
            conditions.Add("detected = {detected:UInt8}");
            parameters["detected"] = detected.Value ? (byte)1 : (byte)0;
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        return (whereClause, parameters);
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildScopedWhereClause(
        IReadOnlyList<long> deviceIds, long? deviceIdFilter,
        DateTime? from, DateTime? to, bool? detected)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (deviceIdFilter.HasValue)
        {
            // Specific device requested (already validated against allowed list)
            conditions.Add("device_id = {device_id:UInt16}");
            parameters["device_id"] = Convert.ToUInt16(deviceIdFilter.Value);
        }
        else
        {
            // All allowed devices — use IN clause
            var idList = string.Join(",", deviceIds);
            conditions.Add($"device_id IN ({idList})");
        }

        if (from.HasValue)
        {
            conditions.Add("timestamp >= {from_ts:DateTime}");
            parameters["from_ts"] = from.Value;
        }

        if (to.HasValue)
        {
            conditions.Add("timestamp <= {to_ts:DateTime}");
            parameters["to_ts"] = to.Value;
        }

        if (detected.HasValue)
        {
            conditions.Add("detected = {detected:UInt8}");
            parameters["detected"] = detected.Value ? (byte)1 : (byte)0;
        }

        var whereClause = "WHERE " + string.Join(" AND ", conditions);
        return (whereClause, parameters);
    }

    private static void AddParameters(ClickHouseCommand cmd, Dictionary<string, object> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            cmd.Parameters.Add(param);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);
    }
}
