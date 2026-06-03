'use client';

import useSWR from 'swr';
import { apiFetch } from '@/lib/fetcher';
import type { RadarLog, PaginatedResponse } from '@/types/api';

interface UseLogsParams {
  deviceId: number | null;
  startTime?: string | null;
  endTime?: string | null;
  detectedOnly?: boolean;
  page?: number;
  limit?: number;
}

/**
 * useLogs — fetches paginated radar log entries from LogService via ClickHouse.
 *
 * Route: GET /api/proxy/logs?device_id=xxx&start_time=...&end_time=...&limit=50&page=1
 * (claude.md §6 — LogService controller)
 *
 * LogService enforces device-scoping for Monitor role:
 *   Monitor → validates device_id is in monitor:devices:{userId} Redis Set → 403 if not
 *   Admin   → no scoping
 *
 * Returns null (no fetch) when deviceId is not selected.
 */
export function useLogs({
  deviceId,
  startTime,
  endTime,
  detectedOnly,
  page = 1,
  limit = 50,
}: UseLogsParams) {
  // Build the query string — only fetch when deviceId is selected
  const queryKey = deviceId
    ? buildUrl(deviceId, startTime, endTime, detectedOnly, page, limit)
    : null;

  const { data, error, isLoading, mutate } = useSWR<PaginatedResponse<RadarLog>>(
    queryKey,
    apiFetch<PaginatedResponse<RadarLog>>,
    {
      revalidateOnFocus: false,
      keepPreviousData: true, // Don't flash loading state on page change
    }
  );

  return {
    logs: data?.items ?? [],
    metadata: { total: data?.totalCount ?? 0, page: data?.page ?? 1, limit: data?.pageSize ?? limit },
    isLoading,
    isError: !!error,
    error,
    mutate,
  };
}

function buildUrl(
  deviceId: number,
  startTime?: string | null,
  endTime?: string | null,
  detectedOnly?: boolean,
  page?: number,
  limit?: number
): string {
  const params = new URLSearchParams();
  params.set('device_id', String(deviceId));
  if (startTime) params.set('from', startTime);
  if (endTime) params.set('to', endTime);
  if (detectedOnly) params.set('detected', 'true');
  if (page) params.set('page', String(page));
  if (limit) params.set('pageSize', String(limit));
  return `/api/proxy/logs?${params.toString()}`;
}
