'use client';

import React from 'react';
import { Row, Col, Card, Statistic, Tag, Typography, Divider, Spin } from 'antd';
import {
  RadarChartOutlined, AlertOutlined,
  CheckCircleOutlined, ClockCircleOutlined,
} from '@ant-design/icons';
import { useAuth } from '@/hooks/useAuth';
import { useDevices } from '@/hooks/useDevices';
import { StatusBadge } from '@/components/StatusBadge';

const { Title, Text } = Typography;

export default function DashboardPage() {
  const { user } = useAuth();
  const { devices, isLoading, onlineCount, offlineCount, errorCount, totalCount } = useDevices();

  // Top 5 most recently seen devices
  const recentDevices = [...devices]
    .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
    .slice(0, 6);

  // Count today's detections from latestLog
  const detectionsToday = devices.filter((d) => d.latestLog?.detected).length;

  const kpiCards = [
    {
      title: 'ACTIVE DEVICES',
      value: isLoading ? '—' : onlineCount,
      prefix: <RadarChartOutlined style={{ color: 'var(--uav-color-primary)' }} />,
      valueStyle: { color: 'var(--uav-color-primary)', fontSize: 28, fontWeight: 700 },
      description: `of ${totalCount} total`,
      borderColor: 'var(--uav-color-primary)',
    },
    {
      title: 'DETECTING NOW',
      value: isLoading ? '—' : detectionsToday,
      prefix: <AlertOutlined style={{ color: 'var(--uav-status-offline)' }} />,
      valueStyle: { color: detectionsToday > 0 ? 'var(--uav-status-offline)' : 'var(--uav-text-primary)', fontSize: 28, fontWeight: 700 },
      description: 'devices with active detection',
      borderColor: 'var(--uav-status-offline)',
    },
    {
      title: 'OFFLINE',
      value: isLoading ? '—' : offlineCount,
      prefix: <ClockCircleOutlined style={{ color: 'var(--uav-status-error)' }} />,
      valueStyle: { color: offlineCount > 0 ? 'var(--uav-status-error)' : 'var(--uav-text-primary)', fontSize: 28, fontWeight: 700 },
      description: 'devices unreachable',
      borderColor: 'var(--uav-status-error)',
    },
    {
      title: 'ERROR STATE',
      value: isLoading ? '—' : errorCount,
      prefix: <CheckCircleOutlined style={{ color: 'var(--uav-status-error)' }} />,
      valueStyle: { color: errorCount > 0 ? 'var(--uav-status-error)' : 'var(--uav-text-primary)', fontSize: 28, fontWeight: 700 },
      description: 'devices in error state',
      borderColor: 'var(--uav-status-error)',
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 28 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 4 }}>
          <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>
            System Overview
          </Title>
          <Tag color="cyan" style={{ fontWeight: 600, letterSpacing: '0.06em' }}>LIVE</Tag>
        </div>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Welcome back{user?.email ? `, ${user.email}` : ''}. Monitoring {totalCount} radar device{totalCount !== 1 ? 's' : ''}.
        </Text>
      </div>

      {/* KPI Stat Cards */}
      <Row gutter={[16, 16]}>
        {kpiCards.map((card) => (
          <Col key={card.title} xs={24} sm={12} xl={6}>
            <Card size="small" style={{ borderTop: `2px solid ${card.borderColor}`, height: '100%' }}>
              <Spin spinning={isLoading} size="small">
                <Statistic
                  title={<Text style={{ fontSize: 11, color: 'var(--uav-text-secondary)', letterSpacing: '0.06em' }}>{card.title}</Text>}
                  value={card.value}
                  prefix={card.prefix}
                  valueStyle={card.valueStyle}
                />
                <Text type="secondary" style={{ fontSize: 11, marginTop: 4, display: 'block' }}>{card.description}</Text>
              </Spin>
            </Card>
          </Col>
        ))}
      </Row>

      <Divider style={{ borderColor: 'var(--uav-border)', margin: '28px 0 24px' }} />

      {/* Device Status Grid */}
      <Row gutter={[16, 16]}>
        <Col xs={24}>
          <Card
            size="small"
            title={
              <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <RadarChartOutlined style={{ color: 'var(--uav-color-primary)' }} />
                <span>Device Status Snapshot</span>
                <Text type="secondary" style={{ fontSize: 11, fontWeight: 400 }}>— updates every 10s</Text>
              </span>
            }
          >
            <Spin spinning={isLoading && devices.length === 0}>
              {recentDevices.length === 0 && !isLoading ? (
                <Text type="secondary" style={{ display: 'block', textAlign: 'center', padding: '32px 0', fontSize: 13 }}>
                  No devices assigned to your account yet.
                </Text>
              ) : (
                <Row gutter={[12, 12]}>
                  {recentDevices.map((device) => (
                    <Col key={device.deviceId} xs={24} sm={12} lg={8} xl={6}>
                      <Card
                        size="small"
                        style={{
                          borderColor: device.status === 'Online' ? 'rgba(34,197,94,0.2)' :
                                       device.status === 'Error' ? 'rgba(245,158,11,0.2)' :
                                       'var(--uav-border)',
                          background: 'var(--uav-bg-elevated)',
                        }}
                      >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
                          <div>
                            <Text style={{ fontSize: 13, fontWeight: 600, display: 'block' }}>{device.locationName}</Text>
                            <Text type="secondary" style={{ fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}>#{device.deviceId}</Text>
                          </div>
                          <StatusBadge status={device.status} size="small" />
                        </div>
                        {device.latestLog && (
                          <div style={{ fontSize: 11, color: 'var(--uav-text-muted)' }}>
                            {device.latestLog.detected ? (
                              <span style={{ color: 'var(--uav-status-offline)', fontWeight: 600 }}>
                                ⚠ {device.latestLog.droneType ?? 'Unknown'} detected
                              </span>
                            ) : (
                              <span>Clear — {Math.round(device.latestLog.accuracy * 100)}% confidence</span>
                            )}
                          </div>
                        )}
                      </Card>
                    </Col>
                  ))}
                </Row>
              )}
            </Spin>
          </Card>
        </Col>
      </Row>
    </div>
  );
}
