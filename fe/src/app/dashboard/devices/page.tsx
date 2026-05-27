'use client';

import React, { useState } from 'react';
import {
  Card, Table, Input, Button, Typography, Statistic,
  Tooltip, Progress, Empty, Spin, Alert,
} from 'antd';
import {
  RadarChartOutlined, SearchOutlined, ReloadOutlined,
  EnvironmentOutlined, ClockCircleOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useDevices } from '@/hooks/useDevices';
import { StatusBadge, DetectionBadge } from '@/components/StatusBadge';
import type { Device } from '@/types/api';
import styles from './page.module.css';

const { Text, Title } = Typography;

function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const seconds = Math.floor(diff / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return new Date(iso).toLocaleDateString();
}

export default function DevicesPage() {
  const { devices, isLoading, isError, error, mutate, onlineCount, offlineCount, errorCount, totalCount } = useDevices();
  const [searchText, setSearchText] = useState('');

  const filteredDevices = devices.filter(
    (d) =>
      d.locationName.toLowerCase().includes(searchText.toLowerCase()) ||
      String(d.deviceId).includes(searchText)
  );

  const columns: ColumnsType<Device> = [
    {
      title: 'Device',
      key: 'device',
      width: 200,
      render: (_, record) => (
        <div className={styles.locationCell}>
          <span className={styles.locationName}>{record.locationName}</span>
          <span className={styles.deviceIdLabel}>ID: #{record.deviceId}</span>
        </div>
      ),
    },
    {
      title: 'Status',
      dataIndex: 'status',
      key: 'status',
      width: 110,
      filters: [
        { text: 'Online', value: 'Online' },
        { text: 'Offline', value: 'Offline' },
        { text: 'Error', value: 'Error' },
      ],
      onFilter: (value, record) => record.status === value,
      render: (status) => <StatusBadge status={status} />,
    },
    {
      title: 'Last Detection',
      key: 'lastDetection',
      width: 180,
      render: (_, record) => {
        if (!record.latestLog) {
          return <Text type="secondary" style={{ fontSize: 12 }}>No data</Text>;
        }
        return (
          <div className={styles.latestLogCell}>
            <DetectionBadge detected={record.latestLog.detected} />
            {record.latestLog.detected && record.latestLog.droneType && (
              <span className={styles.droneType}>{record.latestLog.droneType}</span>
            )}
          </div>
        );
      },
    },
    {
      title: 'Accuracy',
      key: 'accuracy',
      width: 130,
      render: (_, record) => {
        if (!record.latestLog) return <Text type="secondary" style={{ fontSize: 12 }}>—</Text>;
        const pct = Math.round(record.latestLog.accuracy * 100);
        return (
          <div className={styles.accuracyBar}>
            <Progress
              percent={pct}
              size="small"
              showInfo={false}
              strokeColor={pct >= 90 ? '#22C55E' : pct >= 70 ? '#F59E0B' : '#EF4444'}
              style={{ width: 60, margin: 0 }}
            />
            <span>{pct}%</span>
          </div>
        );
      },
    },
    {
      title: 'Control State',
      key: 'controlState',
      width: 130,
      render: (_, record) => (
        <Text style={{ fontSize: 12, fontFamily: 'JetBrains Mono, monospace' }} type="secondary">
          {record.latestLog?.controlState ?? '—'}
        </Text>
      ),
    },
    {
      title: 'Last Seen',
      key: 'lastSeen',
      width: 110,
      render: (_, record) => (
        <Tooltip title={new Date(record.updatedAt).toLocaleString()}>
          <span style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, color: 'var(--uav-text-muted)' }}>
            <ClockCircleOutlined />
            {formatRelativeTime(record.updatedAt)}
          </span>
        </Tooltip>
      ),
      sorter: (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
      defaultSortOrder: 'ascend',
    },
  ];

  return (
    <div>
      {/* Page header */}
      <div style={{ marginBottom: 24 }}>
        <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>
          Radar Devices
        </Title>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Real-time status of all radar hardware nodes. Updates every 10 seconds.
        </Text>
      </div>

      {/* Error banner */}
      {isError && (
        <Alert
          type="error"
          message={`Failed to load devices: ${(error as Error)?.message ?? 'Unknown error'}`}
          style={{ marginBottom: 16 }}
          action={<Button size="small" onClick={() => mutate()}>Retry</Button>}
        />
      )}

      {/* KPI row */}
      <div className={styles.statsRow}>
        <Card size="small" className={styles.statCard} style={{ borderTop: '2px solid var(--uav-status-online)' }}>
          <Statistic title={<Text style={{ fontSize: 11, color: 'var(--uav-text-secondary)', letterSpacing: '0.06em' }}>ONLINE</Text>}
            value={onlineCount} valueStyle={{ color: 'var(--uav-status-online)', fontSize: 24, fontWeight: 700 }} />
        </Card>
        <Card size="small" className={styles.statCard} style={{ borderTop: '2px solid var(--uav-status-offline)' }}>
          <Statistic title={<Text style={{ fontSize: 11, color: 'var(--uav-text-secondary)', letterSpacing: '0.06em' }}>OFFLINE</Text>}
            value={offlineCount} valueStyle={{ color: 'var(--uav-status-offline)', fontSize: 24, fontWeight: 700 }} />
        </Card>
        <Card size="small" className={styles.statCard} style={{ borderTop: '2px solid var(--uav-status-error)' }}>
          <Statistic title={<Text style={{ fontSize: 11, color: 'var(--uav-text-secondary)', letterSpacing: '0.06em' }}>ERROR</Text>}
            value={errorCount} valueStyle={{ color: 'var(--uav-status-error)', fontSize: 24, fontWeight: 700 }} />
        </Card>
        <Card size="small" className={styles.statCard} style={{ borderTop: '2px solid var(--uav-border)' }}>
          <Statistic title={<Text style={{ fontSize: 11, color: 'var(--uav-text-secondary)', letterSpacing: '0.06em' }}>TOTAL</Text>}
            value={totalCount} valueStyle={{ fontSize: 24, fontWeight: 700 }} />
        </Card>
      </div>

      {/* Table */}
      <Card
        size="small"
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <RadarChartOutlined style={{ color: 'var(--uav-color-primary)' }} />
            <span>Device Registry</span>
          </span>
        }
        extra={
          <div className={styles.toolbar}>
            <Input
              id="devices-search"
              prefix={<SearchOutlined style={{ color: 'var(--uav-text-muted)' }} />}
              placeholder="Search by location or ID..."
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              allowClear
              size="small"
              className={styles.searchInput}
            />
            <Tooltip title="Refresh">
              <Button
                id="devices-refresh"
                size="small"
                icon={<ReloadOutlined />}
                onClick={() => mutate()}
                loading={isLoading}
              />
            </Tooltip>
          </div>
        }
      >
        <Spin spinning={isLoading && devices.length === 0}>
          <Table<Device>
            dataSource={filteredDevices}
            columns={columns}
            rowKey="deviceId"
            size="small"
            pagination={{
              pageSize: 20,
              showSizeChanger: true,
              showTotal: (total) => (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {total} device{total !== 1 ? 's' : ''}
                </Text>
              ),
            }}
            locale={{
              emptyText: (
                <div className={styles.emptyState}>
                  <Empty
                    image={<EnvironmentOutlined style={{ fontSize: 40, color: 'var(--uav-border)' }} />}
                    description={
                      <Text type="secondary">
                        {searchText ? 'No devices match your search' : 'No devices assigned to your account'}
                      </Text>
                    }
                  />
                </div>
              ),
            }}
            rowClassName={(record) =>
              record.status === 'Error' ? 'ant-table-row-warning' : ''
            }
          />
        </Spin>
      </Card>
    </div>
  );
}
