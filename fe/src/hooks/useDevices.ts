'use client';

import useSWR from 'swr';
import { apiFetch } from '@/lib/fetcher';
import { useAuth } from '@/hooks/useAuth';
import type { Device } from '@/types/api';

/**
 * useDevices — fetches the list of radar devices visible to the current user.
 *
 * Routing (claude.md §2 — Kong routing matrix):
 *   Monitor role → GET /api/proxy/devices       → Kong → DeviceService /api/v1/devices
 *   Admin role   → GET /api/proxy/admin/devices  → Kong → DeviceService /api/v1/admin/devices
 *
 * DeviceService merges Redis device:latest_log:{id} into each result.
 * SWR refreshes every 10 seconds for near-real-time device status updates.
 */
export function useDevices() {
  const { user } = useAuth();

  const endpoint = user?.role === 'Admin'
    ? '/api/proxy/admin/devices'
    : '/api/proxy/devices';

  // Only start fetching once we know the user's role
  const { data, error, isLoading, mutate } = useSWR<Device[]>(
    user ? endpoint : null,
    apiFetch<Device[]>,
    {
      refreshInterval: 10_000, // Poll every 10s for live status updates
      revalidateOnFocus: true,
      dedupingInterval: 5_000,
    }
  );

  const onlineCount = data?.filter((d) => d.status === 'Online').length ?? 0;
  const offlineCount = data?.filter((d) => d.status === 'Offline').length ?? 0;
  const errorCount = data?.filter((d) => d.status === 'Error').length ?? 0;

  return {
    devices: data ?? [],
    isLoading,
    isError: !!error,
    error,
    mutate,
    // Convenience counts for the dashboard KPI tiles
    onlineCount,
    offlineCount,
    errorCount,
    totalCount: data?.length ?? 0,
  };
}
