'use client';

import React, { useState } from 'react';
import useSWR from 'swr';
import {
  Card, Table, Button, Typography, Tag, Input,
  Modal, Form, Select, Tooltip, Alert, Spin, Popconfirm, Empty,
} from 'antd';
import {
  TeamOutlined, PlusOutlined, SearchOutlined,
  ReloadOutlined, UserOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { apiFetch, apiPost } from '@/lib/fetcher';
import type { AppUser } from '@/types/api';
import styles from './page.module.css';

const { Text, Title } = Typography;

interface CreateUserForm {
  name: string;
  email: string;
  password: string;
  role: 'Admin' | 'Monitor';
}

export default function AdminUsersPage() {
  const { data: users, isLoading, error, mutate } = useSWR<AppUser[]>(
    '/api/proxy/admin/users',
    apiFetch<AppUser[]>,
    { revalidateOnFocus: false }
  );

  const [searchText, setSearchText] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [form] = Form.useForm<CreateUserForm>();

  const filtered = (users ?? []).filter(
    (u) =>
      u.name.toLowerCase().includes(searchText.toLowerCase()) ||
      u.email.toLowerCase().includes(searchText.toLowerCase())
  );

  async function handleCreate(values: CreateUserForm) {
    setCreating(true);
    setCreateError(null);
    try {
      await apiPost('/api/proxy/admin/users', values);
      await mutate();
      setModalOpen(false);
      form.resetFields();
    } catch (e) {
      setCreateError((e as Error).message ?? 'Failed to create user.');
    } finally {
      setCreating(false);
    }
  }

  const columns: ColumnsType<AppUser> = [
    {
      title: 'User',
      key: 'user',
      render: (_, record) => (
        <div className={styles.userCell}>
          <span className={styles.userName}>{record.name}</span>
          <span className={styles.userEmail}>{record.email}</span>
        </div>
      ),
    },
    {
      title: 'Role',
      dataIndex: 'role',
      key: 'role',
      width: 110,
      filters: [
        { text: 'Admin', value: 'Admin' },
        { text: 'Monitor', value: 'Monitor' },
      ],
      onFilter: (value, record) => record.role === value,
      render: (role: 'Admin' | 'Monitor') => (
        <Tag
          color={role === 'Admin' ? 'cyan' : 'blue'}
          className={styles.roleTag}
        >
          {role.toUpperCase()}
        </Tag>
      ),
    },
    {
      title: 'User ID',
      dataIndex: 'id',
      key: 'id',
      width: 300,
      render: (id: string) => <span className={styles.idCell}>{id}</span>,
    },
    {
      title: 'Created',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      sorter: (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
      render: (ts: string) => (
        <Text type="secondary" style={{ fontSize: 12 }}>
          {new Date(ts).toLocaleDateString('en-GB', { dateStyle: 'medium' })}
        </Text>
      ),
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 24 }}>
        <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>
          User Management
        </Title>
        <Text type="secondary" style={{ fontSize: 13 }}>
          Create and manage operator accounts. Admin-only.
        </Text>
      </div>

      {error && (
        <Alert type="error" message={`Failed to load users: ${error.message}`}
          style={{ marginBottom: 16 }}
          action={<Button size="small" onClick={() => mutate()}>Retry</Button>} />
      )}

      <Card
        size="small"
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <TeamOutlined style={{ color: 'var(--uav-color-primary)' }} />
            <span>Operator Accounts</span>
            {users && (
              <Tag color="default" style={{ fontSize: 11, margin: 0 }}>
                {users.length} user{users.length !== 1 ? 's' : ''}
              </Tag>
            )}
          </span>
        }
        extra={
          <div className={styles.toolbar}>
            <Input
              id="users-search"
              prefix={<SearchOutlined style={{ color: 'var(--uav-text-muted)' }} />}
              placeholder="Search by name or email..."
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
              id="users-create"
              type="primary"
              size="small"
              icon={<PlusOutlined />}
              onClick={() => setModalOpen(true)}
            >
              New User
            </Button>
          </div>
        }
      >
        <Spin spinning={isLoading && !users}>
          <Table<AppUser>
            dataSource={filtered}
            columns={columns}
            rowKey="id"
            size="small"
            pagination={{ pageSize: 20, showSizeChanger: false }}
            locale={{
              emptyText: (
                <div className={styles.emptyState}>
                  <Empty
                    image={<UserOutlined style={{ fontSize: 40, color: 'var(--uav-border)' }} />}
                    description={<Text type="secondary">No users found</Text>}
                  />
                </div>
              ),
            }}
          />
        </Spin>
      </Card>

      {/* Create User Modal */}
      <Modal
        title={
          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <PlusOutlined style={{ color: 'var(--uav-color-primary)' }} />
            Create New Operator Account
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
        <Form form={form} layout="vertical" onFinish={handleCreate} requiredMark={false}>
          <Form.Item name="name" label="Full Name"
            rules={[{ required: true, message: 'Name is required.' }]}>
            <Input id="create-user-name" placeholder="John Smith" />
          </Form.Item>
          <Form.Item name="email" label="Email Address"
            rules={[{ required: true, message: 'Email is required.' }, { type: 'email', message: 'Enter a valid email.' }]}>
            <Input id="create-user-email" placeholder="operator@uav-system.io" />
          </Form.Item>
          <Form.Item name="password" label="Initial Password"
            rules={[{ required: true, message: 'Password is required.' }, { min: 8, message: 'Minimum 8 characters.' }]}>
            <Input.Password id="create-user-password" placeholder="Min. 8 characters" />
          </Form.Item>
          <Form.Item name="role" label="Role" initialValue="Monitor"
            rules={[{ required: true }]}>
            <Select id="create-user-role" options={[
              { value: 'Monitor', label: 'Monitor — Read-only, scoped to assigned devices' },
              { value: 'Admin', label: 'Admin — Full system access' },
            ]} />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0, textAlign: 'right' }}>
            <Button onClick={() => setModalOpen(false)} style={{ marginRight: 8 }}>Cancel</Button>
            <Button type="primary" htmlType="submit" loading={creating}>Create Account</Button>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
