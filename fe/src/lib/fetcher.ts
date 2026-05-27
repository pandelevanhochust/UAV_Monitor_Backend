/**
 * SWR fetcher utility for the UAV proxy routes.
 *
 * All authenticated API calls go through /api/proxy/[...path] which:
 * 1. Reads the uav_token HttpOnly cookie server-side.
 * 2. Injects Authorization: Bearer <token> into the upstream request to Kong.
 * 3. Returns the upstream response (with original status code).
 *
 * Usage with SWR:
 *   const { data } = useSWR('/api/proxy/devices', apiFetch);
 */

import type { ApiError } from '@/types/api';

export class ApiResponseError extends Error {
  status: number;
  detail: string | undefined;

  constructor(message: string, status: number, detail?: string) {
    super(message);
    this.name = 'ApiResponseError';
    this.status = status;
    this.detail = detail;
  }
}

/**
 * apiFetch — throws ApiResponseError on non-2xx responses.
 * Designed to be used as the SWR `fetcher` argument.
 */
export async function apiFetch<T = unknown>(url: string): Promise<T> {
  const res = await fetch(url, { cache: 'no-store' });

  if (!res.ok) {
    let message = `Request failed with status ${res.status}`;
    let detail: string | undefined;

    try {
      const err: ApiError = await res.json();
      message = err.message ?? err.title ?? message;
      detail = err.detail;
    } catch {
      // Non-JSON error body — use default message
    }

    throw new ApiResponseError(message, res.status, detail);
  }

  // 204 No Content — return null
  if (res.status === 204) {
    return null as T;
  }

  return res.json() as Promise<T>;
}

/**
 * apiPost — sends JSON body and returns parsed JSON response.
 * For mutation operations (POST / PUT / DELETE) from Client Components.
 */
export async function apiPost<TBody = unknown, TResponse = unknown>(
  url: string,
  body: TBody,
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE' = 'POST'
): Promise<TResponse> {
  const res = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: method !== 'DELETE' ? JSON.stringify(body) : undefined,
    cache: 'no-store',
  });

  if (!res.ok) {
    let message = `Request failed with status ${res.status}`;
    try {
      const err: ApiError = await res.json();
      message = err.message ?? err.title ?? message;
    } catch { /* ignore */ }
    throw new ApiResponseError(message, res.status);
  }

  if (res.status === 204) return null as TResponse;
  return res.json() as Promise<TResponse>;
}
