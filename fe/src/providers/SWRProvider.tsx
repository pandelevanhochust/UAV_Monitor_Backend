'use client';

import React from 'react';
import { SWRConfig } from 'swr';
import { App as AntApp, message, notification } from 'antd';
import { ApiResponseError } from '@/lib/fetcher';

interface SWRProviderProps {
  children: React.ReactNode;
}

/**
 * SWRProvider
 *
 * Configures global SWR behaviour:
 *  - `onError`         — Shows an Ant Design notification for unexpected API errors
 *                        (401 is silently swallowed — handled by proxy.ts redirect)
 *  - `revalidateOnFocus` — true globally; individual hooks can override per-key
 *  - `shouldRetryOnError` — false for 4xx (client errors); true for 5xx (transient)
 *  - `dedupingInterval`   — 3s default to prevent burst refetches on component remount
 *
 * Also wraps with Ant Design's `App` context which provides imperative access to
 * `message`, `modal`, and `notification` APIs from any component.
 */
export function SWRProvider({ children }: SWRProviderProps) {
  return (
    <AntApp>
      <SWRConfig
        value={{
          revalidateOnFocus: true,
          revalidateOnReconnect: true,
          dedupingInterval: 3000,
          shouldRetryOnError: (error: unknown) => {
            // Don't retry client errors (4xx) — they won't resolve on their own
            if (error instanceof ApiResponseError && error.status < 500) {
              return false;
            }
            return true;
          },
          errorRetryCount: 3,
          errorRetryInterval: 5000,
          onError: (error: unknown) => {
            // 401 → proxy.ts/middleware will redirect to /login — no notification needed
            if (error instanceof ApiResponseError && error.status === 401) return;

            // Surface unexpected errors via Ant Design notification
            const msg =
              error instanceof ApiResponseError
                ? error.message
                : 'An unexpected error occurred. Please try again.';

            notification.error({
              message: 'Data Fetch Error',
              description: msg,
              placement: 'bottomRight',
              duration: 5,
            });
          },
        }}
      >
        {children}
      </SWRConfig>
    </AntApp>
  );
}
