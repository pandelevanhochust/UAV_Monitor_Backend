'use client';

import React, { useState } from 'react';
import {
  Card, Table, Select, Button, Typography, DatePicker,
  Switch, Tag, Tooltip, Empty, Spin, Alert,
} from 'antd';
import {
  FilterOutlined, ReloadOutlined,
  UnorderedListOutlined, SearchOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import { useDevices } from '@/hooks/useDevices';
import { useLogs } from '@/hooks/useLogs';
import { DetectionBadge } from '@/components/StatusBadge';
import type { RadarLog } from '@/types/api';
import styles from './page.module.css';

const { Text, Title } = Typography;
const { RangePicker } = DatePicker;

export default function LogsPage() {
  const { devices, isLoading: devicesLoading } = useDevices();
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [detectedOnly, setDetectedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;

  const startTime = dateRange?.[0]?.toISOString() ?? null;
  const endTime = dateRange?.[1]?.toISOString() ?? null;

  const { logs, metadata, isLoading, isError, error, mutate } = useLogs({
    deviceId: selectedDeviceId,
    startTime,
    endTime,
    detectedOnly,
    page,
    limit: PAGE_SIZE,
  });

  const columns: ColumnsType<RadarLog> = [
    {
      title: 'Timestamp',
      dataIndex: 'timestamp',
      key: 'timestamp',
      width: 180,
      render: (ts: string) => (
        <span className={styles.timestampCell}>
          {new Date(ts).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'medium' })}
        </span>
      ),
    },
    {
      title: 'Device',
      dataIndex: 'deviceId',
      key: 'deviceId',
      width: 80,
      render: (id: number) => (
        <Text style={{ fontSize: 12, fontFamily: 'JetBrains Mono, monospace' }}>#{id}</Text>
      ),
    },
    {
      title: 'Detection',
      dataIndex: 'detected',
      key: 'detected',
      width: 110,
      render: (detected: boolean) => <DetectionBadge detected={detected} />,
    },
    {
      title: 'Drone Type',
      dataIndex: 'droneType',
      key: 'droneType',
      width: 120,
      render: (type: string | null) =>
        type ? (
          <span className={styles.droneTypeCell}>{type}</span>
        ) : (
          <Text type="secondary" style={{ fontSize: 12 }}>—</Text>
        ),
    },
    {
      title: 'Control State',
      dataIndex: 'controlState',
      key: 'controlState',
      width: 130,
      render: (s: string | null) => (
        <Text style={{ fontSize: 12, fontFamily: 'JetBrains Mono, monospace' }} type="secondary">
          {s ?? '—'}
        </Text>
      ),
    },
    {
      title: 'Accuracy',
      dataIndex: 'accuracy',
      key: 'accuracy',
      width: 85,
      sorter: (a, b) => a.accuracy - b.accuracy,
      render: (acc: number) => {
        const pct = Math.round(acc * 100);
        const color = pct >= 90 ? 'var(--uav-status-online)' : pct >= 70 ? 'var(--uav-status-error)' : 'var(--uav-status-offline)';
        return <span className={styles.accuracyCell} style={{ color }}>{pct}%</span>;
      },
    },
    {
      title: 'Status',
      dataIndex: 'status',
      key: 'status',
      width: 90,
      render: (s: string) => <Tag style={{ margin: 0, fontSize: 10, letterSpacing: '0.06em' }}>{s.toUpperCase()}</Tag>,
    },
  ];

  const deviceOptions = devices.map((d) => ({ value: d.deviceId, label: `#${d.deviceId} — ${d.locationName}` }));

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>Detection Logs</Title>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Historical radar scan records from ClickHouse. Select a device to begin.
        </Text>
      </div>

      {isError && (
        <Alert type="error" message={`Failed to load logs: ${(error as Error)?.message ?? 'Unknown error'}`}
          style={{ marginBottom: 16 }}
          action={<Button size="small" onClick={() => mutate()}>Retry</Button>} />
      )}

      <div className={styles.filterBar}>
        <span className={styles.filterLabel}>Device:</span>
        <Select id="logs-device-select" className={styles.deviceSelect} placeholder="Select a radar device..."
          options={deviceOptions} loading={devicesLoading} onChange={(v) => { setSelectedDeviceId(v); setPage(1); }}
          allowClear onClear={() => setSelectedDeviceId(null)} showSearch
          filterOption={(input, option) => String(option?.label ?? '').toLowerCase().includes(input.toLowerCase())} />
        <div className={styles.filterDivider} />
        <span className={styles.filterLabel}>Time Range:</span>
        <RangePicker id="logs-date-range" className={styles.rangePicker} showTime
          onChange={(val) => { setDateRange(val); setPage(1); }} />
        <div className={styles.filterDivider} />
        <span className={styles.filterLabel}>Detections only:</span>
        <Switch id="logs-detected-toggle" size="small" checked={detectedOnly}
          onChange={(v) => { setDetectedOnly(v); setPage(1); }} />
        <div className={styles.filterSpacer} />
        <Tooltip title="Refresh">
          <Button id="logs-refresh" size="small" icon={<ReloadOutlined />} onClick={() => mutate()}
            loading={isLoading} disabled={!selectedDeviceId} />
        </Tooltip>
      </div>

      <Card size="small" className={styles.logTableWrapper}
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <UnorderedListOutlined style={{ color: 'var(--uav-color-primary)' }} />
            <span>Radar Scan Records</span>
            {metadata.total > 0 && <Tag color="default" style={{ fontSize: 11, margin: 0 }}>{metadata.total.toLocaleString()} records</Tag>}
          </span>
        }>
        <Spin spinning={isLoading}>
          <Table<RadarLog> dataSource={logs} columns={columns} rowKey={(r) => `${r.deviceId}-${r.timestamp}`}
            size="small" scroll={{ x: 1000 }}
            pagination={{ current: page, pageSize: PAGE_SIZE, total: metadata.total, onChange: setPage, showSizeChanger: false }}
            locale={{
              emptyText: (
                <div className={styles.emptyHint}>
                  {!selectedDeviceId ? (
                    <Empty image={<SearchOutlined style={{ fontSize: 40, color: 'var(--uav-border)' }} />}
                      description={<Text type="secondary">Select a device to query logs</Text>} />
                  ) : (
                    <Empty image={<FilterOutlined style={{ fontSize: 40, color: 'var(--uav-border)' }} />}
                      description={<Text type="secondary">No logs found for the selected filters</Text>} />
                  )}
                </div>
              ),
            }} />
        </Spin>
      </Card>
    </div>
  );
}
