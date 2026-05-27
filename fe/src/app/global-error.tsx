'use client';

import React, { useEffect } from 'react';
import { Button, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';

const { Title, Text } = Typography;

interface GlobalErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

/**
 * global-error.tsx — App Router global error boundary.
 * Catches errors in the root layout itself (e.g. provider failures).
 * Must render its own <html> and <body> tags because it replaces the root layout.
 */
export default function GlobalError({ error, reset }: GlobalErrorProps) {
  useEffect(() => {
    console.error('[Global Error]', error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          padding: 0,
          minHeight: '100vh',
          background: '#0A0D14',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontFamily: "'JetBrains Mono', 'Courier New', monospace",
        }}
      >
        <div style={{ textAlign: 'center', padding: 32 }}>
          {/* Minimal radar icon */}
          <div
            style={{
              width: 64,
              height: 64,
              borderRadius: '50%',
              border: '2px solid #EF4444',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              margin: '0 auto 24px',
              fontSize: 28,
              color: '#EF4444',
            }}
          >
            ⚠
          </div>

          <Title
            level={2}
            style={{ color: '#E2E8F0', margin: '0 0 8px', fontSize: 22 }}
          >
            Critical System Error
          </Title>
          <Text
            style={{
              display: 'block',
              color: '#94A3B8',
              fontSize: 13,
              marginBottom: 8,
            }}
          >
            {error.message || 'The application encountered an unrecoverable error.'}
          </Text>
          {error.digest && (
            <Text
              style={{
                display: 'block',
                color: '#64748B',
                fontSize: 11,
                marginBottom: 24,
              }}
            >
              Error ID: {error.digest}
            </Text>
          )}

          <Button
            type="primary"
            icon={<ReloadOutlined />}
            onClick={reset}
            style={{
              background: '#00D4FF',
              borderColor: '#00D4FF',
              color: '#0A0D14',
              fontWeight: 700,
            }}
          >
            Reload System
          </Button>
        </div>
      </body>
    </html>
  );
}
