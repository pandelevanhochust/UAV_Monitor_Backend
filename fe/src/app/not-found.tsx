'use client';

import React from 'react';
import { Button, Result, Typography } from 'antd';
import Link from 'next/link';
import { HomeOutlined, RadarChartOutlined } from '@ant-design/icons';

const { Text } = Typography;

/**
 * Custom 404 — Not Found page.
 * Shown when a route doesn't match any page in the app.
 * Server Component (no 'use client' needed).
 */
export default function NotFound() {
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--uav-bg-base)',
        padding: 24,
      }}
    >
      {/* Logo */}
      <div
        style={{
          width: 56,
          height: 56,
          borderRadius: '50%',
          border: '2px solid var(--uav-color-primary)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'rgba(0, 212, 255, 0.06)',
          boxShadow: '0 0 20px rgba(0, 212, 255, 0.15)',
          fontSize: 24,
          color: 'var(--uav-color-primary)',
          marginBottom: 32,
        }}
      >
        <RadarChartOutlined />
      </div>

      <Result
        status="404"
        title={
          <span style={{ color: 'var(--uav-text-primary)', fontSize: 48, fontWeight: 700 }}>
            404
          </span>
        }
        subTitle={
          <div style={{ textAlign: 'center' }}>
            <Text style={{ color: 'var(--uav-text-secondary)', fontSize: 14 }}>
              Target not found. This coordinate doesn&apos;t exist in the system.
            </Text>
          </div>
        }
        extra={
          <Link href="/dashboard">
            <Button type="primary" icon={<HomeOutlined />} size="large">
              Return to Dashboard
            </Button>
          </Link>
        }
      />
    </div>
  );
}
