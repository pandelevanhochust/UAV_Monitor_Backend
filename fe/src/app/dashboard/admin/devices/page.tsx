'use client';

import React, { useState } from 'react';
import useSWR from 'swr';
import {
  Card, Table, Button, Typography, Input, Modal,
  Form, InputNumber, Select, Tooltip, Alert, Spin,
  Empty, message, Tag,
} from 'antd';
import {
  SettingOutlined, PlusOutlined, SearchOutlined,
  ReloadOutlined, CopyOutlined, WarningOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { apiFetch, apiPost } from '@/lib/fetcher';
import { StatusBadge } from '@/components/StatusBadge';
import type { Device, AppUser } from '@/types/api';
import styles from './page.module.css';

const { Text, Title } = Typography;

interface RegisterDeviceForm {
  deviceId: number;
  locationName: string;
  assignedMonitorId?: string;
}

interface RegisterDeviceResponse {
  deviceId: number;
  locationName: string;
  apiKey: string; // Shown only once — from DeviceService on register
}

export default function AdminDevicesPage() {
  const { data: devices, isLoading, error, mutate } = useSWR<Device[]>(
    '/api/proxy/admin/devices',
    apiFetch<Device[]>,
    { revalidateOnFocus: false }
  );

  // Fetch users for the assign-monitor selector
  const { data: users } = useSWR<AppUser[]>(
    '/api/proxy/admin/users',
    apiFetch<AppUser[]>,
    { revalidateOnFocus: false }
  );

  const [searchText, setSearchText] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [newApiKey, setNewApiKey] = useState<{ deviceId: number; apiKey: string } | null>(null);
  const [form] = Form.useForm<RegisterDeviceForm>();
  const [messageApi, contextHolder] = message.useMessage();

  const monitorUsers = (users ?? []).filter((u) => u.role === 'Monitor');

  const filtered = (devices ?? []).filter(
    (d) =>
      d.locationName.toLowerCase().includes(searchText.toLowerCase()) ||
      String(d.deviceId).includes(searchText)
  );

  async function handleRegister(values: RegisterDeviceForm) {
    setCreating(true);
    setCreateError(null);
    try {
      const result = await apiPost<RegisterDeviceForm, RegisterDeviceResponse>(
        '/api/proxy/admin/devices',
        values
      );
      await mutate();
      setModalOpen(false);
      form.resetFields();
      // Show API key — only displayed once (claude.md §4 AdminDevicesController)
      setNewApiKey({ deviceId: result.deviceId, apiKey: result.apiKey });
    } catch (e) {
      setCreateError((e as Error).message ?? 'Failed to register device.');
    } finally {
      setCreating(false);
    }
  }

  function copyApiKey(key: string) {
    navigator.clipboard.writeText(key);
    messageApi.success('API key copied to clipboard');
  }

  const columns: ColumnsType<Device> = [
    {
      title: 'Device',
      key: 'device',
      render: (_, record) => (
        <div className={styles.deviceCell}>
          <span className={styles.deviceLocation}>{record.locationName}</span>
          <span className={styles.deviceId}>ID: #{record.deviceId}</span>
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
      title: 'Assigned Monitor',
      dataIndex: 'assignedMonitorId',
      key: 'assignedMonitorId',
      render: (monitorId: string | null) => {
        if (!monitorId) return <Text type="secondary" style={{ fontSize: 12 }}>Unassigned</Text>;
        const user = (users ?? []).find((u) => u.id === monitorId);
        return user ? (
          <div>
            <Text style={{ fontSize: 13, fontWeight: 500 }}>{user.name}</Text>
            <br />
            <Text type="secondary" style={{ fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}>{user.email}</Text>
          </div>
        ) : (
          <Text type="secondary" style={{ fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}>
            {monitorId.slice(0, 16)}...
          </Text>
        );
      },
    },
    {
      title: 'Last Updated',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      sorter: (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
      render: (ts: string) => (
        <Text type="secondary" style={{ fontSize: 12 }}>
          {new Date(ts).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' })}
        </Text>
      ),
    },
  ];

  return (
    <div>
      {contextHolder}

      <div style={{ marginBottom: 24 }}>
        <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>
          Device Management
        </Title>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Register radar hardware nodes and assign them to monitor operators. Admin-only.
        </Text>
      </div>

      {error && (
        <Alert type="error" message={`Failed to load devices: ${error.message}`}
          style={{ marginBottom: 16 }}
          action={<Button size="small" onClick={() => mutate()}>Retry</Button>} />
      )}

      {/* One-time API key display */}
      {newApiKey && (
        <Alert
          type="success"
          style={{ marginBottom: 16 }}
          message={
            <span style={{ fontWeight: 600 }}>
              Device #{newApiKey.deviceId} registered — copy the API key now
            </span>
          }
          description={
            <div>
              <div className={styles.apiKeyLabel}>X-DEVICE-API-KEY</div>
              <div className={styles.apiKeyBox}>
                <span className={styles.apiKeyValue}>{newApiKey.apiKey}</span>
                <Tooltip title="Copy">
                  <Button
                    size="small"
                    icon={<CopyOutlined />}
                    onClick={() => copyApiKey(newApiKey.apiKey)}
                    type="text"
                    style={{ color: 'var(--uav-color-primary)' }}
                  />
                </Tooltip>
              </div>
              <div className={styles.apiKeyWarning}>
                <WarningOutlined /> This key will not be shown again. Store it securely on the device.
              </div>
            </div>
          }
          closable
          onClose={() => setNewApiKey(null)}
        />
      )}

      <Card
        size="small"
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <SettingOutlined style={{ color: 'var(--uav-color-primary)' }} />
            <span>Registered Devices</span>
            {devices && (
              <Tag color="default" style={{ fontSize: 11, margin: 0 }}>
                {devices.length} device{devices.length !== 1 ? 's' : ''}
              </Tag>
            )}
          </span>
        }
        extra={
          <div className={styles.toolbar}>
            <Input
              id="admin-devices-search"
              prefix={<SearchOutlined style={{ color: 'var(--uav-text-muted)' }} />}
              placeholder="Search by location or ID..."
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              allowClear
              size="small"
              className={styles.searchInput}
            />
            <Tooltip title="Refresh">
              <Button size="small" icon={<ReloadOutlined />} onClick={() => mutate()} loading={isLoading} />
            </Tooltip>
            <Button
              id="admin-devices-register"
              type="primary"
              size="small"
              icon={<PlusOutlined />}
              onClick={() => setModalOpen(true)}
            >
              Register Device
            </Button>
          </div>
        }
      >
        <Spin spinning={isLoading && !devices}>
          <Table<Device>
            dataSource={filtered}
            columns={columns}
            rowKey="deviceId"
            size="small"
            pagination={{ pageSize: 20, showSizeChanger: false }}
            locale={{
              emptyText: (
                <div className={styles.emptyState}>
                  <Empty
                    image={<SettingOutlined style={{ fontSize: 40, color: 'var(--uav-border)' }} />}
                    description={<Text type="secondary">No devices registered yet</Text>}
                  />
                </div>
              ),
            }}
          />
        </Spin>
      </Card>

      {/* Register Device Modal */}
      <Modal
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <PlusOutlined style={{ color: 'var(--uav-color-primary)' }} />
            Register Radar Device
          </span>
        }
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); setCreateError(null); }}
        footer={null}
        destroyOnClose
      >
        {createError && (
          <Alert type="error" message={createError} style={{ marginBottom: 16 }} closable onClose={() => setCreateError(null)} />
        )}
        <Form form={form} layout="vertical" onFinish={handleRegister} requiredMark={false}>
          <Form.Item name="deviceId" label="Device ID"
            rules={[{ required: true, message: 'Device ID is required.' }]}
            extra="Must match the hardware BIGINT identifier burned into the edge device.">
            <InputNumber
              id="register-device-id"
              style={{ width: '100%' }}
              placeholder="e.g. 1002"
              min={1}
            />
          </Form.Item>
          <Form.Item name="locationName" label="Location Name"
            rules={[{ required: true, message: 'Location is required.' }]}>
            <Input id="register-device-location" placeholder="e.g. North Perimeter Gate" />
          </Form.Item>
          <Form.Item name="assignedMonitorId" label="Assign to Monitor (optional)">
            <Select
              id="register-device-monitor"
              placeholder="Select a monitor operator..."
              allowClear
              showSearch
              options={monitorUsers.map((u) => ({ value: u.id, label: `${u.name} (${u.email})` }))}
              filterOption={(input, option) =>
                String(option?.label ?? '').toLowerCase().includes(input.toLowerCase())
              }
            />
          </Form.Item>
          <Alert
            type="info"
            message="An API key will be generated automatically. It will be displayed only once after registration."
            style={{ marginBottom: 16 }}
            showIcon
          />
          <Form.Item style={{ marginBottom: 0, textAlign: 'right' }}>
            <Button onClick={() => setModalOpen(false)} style={{ marginRight: 8 }}>Cancel</Button>
            <Button type="primary" htmlType="submit" loading={creating}>Register & Generate Key</Button>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
