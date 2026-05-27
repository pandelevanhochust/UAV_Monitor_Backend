'use client';

import React from 'react';
import { Skeleton, Card, Row, Col } from 'antd';

/**
 * Skeleton components for loading states across the dashboard.
 * Uses Ant Design's Skeleton component styled to match Industrial Dark theme.
 * These replace the Spin placeholders during initial data fetch.
 */

// ─── Stat Card Skeleton ────────────────────────────────────────────────────────

export function StatCardSkeleton() {
  return (
    <Card size="small" style={{ borderTop: '2px solid var(--uav-border)' }}>
      <Skeleton active paragraph={{ rows: 1 }} title={{ width: '40%' }} />
    </Card>
  );
}

// ─── Device Row Skeleton ───────────────────────────────────────────────────────

export function DeviceTableSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: '8px 0' }}>
      {Array.from({ length: rows }).map((_, i) => (
        <div
          key={i}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 16,
            padding: '8px 0',
            borderBottom: '1px solid var(--uav-border)',
          }}
        >
          <Skeleton.Avatar active size="small" shape="square" style={{ width: 80, height: 16, borderRadius: 4 }} />
          <Skeleton active title={false} paragraph={{ rows: 1, width: '20%' }} style={{ flex: 1 }} />
          <Skeleton active title={false} paragraph={{ rows: 1, width: '10%' }} style={{ width: 80 }} />
          <Skeleton active title={false} paragraph={{ rows: 1, width: '15%' }} style={{ width: 100 }} />
        </div>
      ))}
    </div>
  );
}

// ─── Dashboard Overview Skeleton ───────────────────────────────────────────────

export function DashboardSkeleton() {
  return (
    <div>
      {/* KPI tiles */}
      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        {[0, 1, 2, 3].map((i) => (
          <Col key={i} xs={24} sm={12} xl={6}>
            <StatCardSkeleton />
          </Col>
        ))}
      </Row>
      {/* Device grid */}
      <Card size="small">
        <DeviceTableSkeleton rows={4} />
      </Card>
    </div>
  );
}

// ─── Alert Feed Skeleton ───────────────────────────────────────────────────────

export function AlertFeedSkeleton({ items = 3 }: { items?: number }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {Array.from({ length: items }).map((_, i) => (
        <Card key={i} size="small" style={{ borderLeft: '3px solid var(--uav-border)' }}>
          <Skeleton active paragraph={{ rows: 2 }} title={{ width: '30%' }} />
        </Card>
      ))}
    </div>
  );
}

// ─── Page Header Skeleton ──────────────────────────────────────────────────────

export function PageHeaderSkeleton() {
  return (
    <div style={{ marginBottom: 24 }}>
      <Skeleton active title={{ width: 200 }} paragraph={{ rows: 1, width: '40%' }} />
    </div>
  );
}
