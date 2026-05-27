'use client';

import React, { useEffect } from 'react';
import { Button, Result, Typography } from 'antd';
import { ReloadOutlined, HomeOutlined } from '@ant-design/icons';
import { useRouter } from 'next/navigation';

const { Text } = Typography;

interface DashboardErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

/**
 * Next.js App Router error boundary for the /dashboard route segment.
 * Catches unhandled errors thrown during rendering of dashboard pages.
 * Must be a Client Component.
 */
export default function DashboardError({ error, reset }: DashboardErrorProps) {
  const router = useRouter();

  useEffect(() => {
    // Log to console in dev; in production wire to Sentry/DataDog etc.
    console.error('[Dashboard Error]', error);
  }, [error]);

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: 'calc(100vh - 64px)',
        padding: 24,
      }}
    >
      <Result
        status="error"
        title="Something went wrong"
        subTitle={
          <div style={{ textAlign: 'center' }}>
            <Text type="secondary" style={{ fontSize: 13 }}>
              {error.message || 'An unexpected error occurred in the dashboard.'}
            </Text>
            {error.digest && (
              <Text
                type="secondary"
                style={{
                  display: 'block',
                  fontSize: 11,
                  marginTop: 8,
                  fontFamily: 'JetBrains Mono, monospace',
                  color: 'var(--uav-text-muted)',
                }}
              >
                Error ID: {error.digest}
              </Text>
            )}
          </div>
        }
        extra={[
          <Button
            key="retry"
            type="primary"
            icon={<ReloadOutlined />}
            onClick={reset}
          >
            Try Again
          </Button>,
          <Button
            key="home"
            icon={<HomeOutlined />}
            onClick={() => router.push('/dashboard')}
          >
            Back to Dashboard
          </Button>,
        ]}
      />
    </div>
  );
}
