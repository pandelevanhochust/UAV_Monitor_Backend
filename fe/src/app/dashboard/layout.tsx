'use client';

import React, { useState } from 'react';
import { Layout, Menu, Button, Tag, Spin, Tooltip } from 'antd';
import {
  DashboardOutlined,
  RadarChartOutlined,
  UnorderedListOutlined,
  UserOutlined,
  SettingOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/hooks/useAuth';
import styles from './layout.module.css';

const { Sider, Header, Content } = Layout;

// ─── Navigation items ─────────────────────────────────────────────────────────

const MONITOR_NAV_ITEMS = [
  {
    key: '/dashboard',
    icon: <DashboardOutlined />,
    label: 'Dashboard',
  },
  {
    key: '/dashboard/devices',
    icon: <RadarChartOutlined />,
    label: 'Radar Devices',
  },
  {
    key: '/dashboard/logs',
    icon: <UnorderedListOutlined />,
    label: 'Detection Logs',
  },
  {
    key: '/dashboard/alerts',
    icon: <SettingOutlined />,
    label: 'Live Alerts',
  },
];

const ADMIN_ONLY_NAV_ITEMS = [
  {
    key: '/dashboard/admin/users',
    icon: <TeamOutlined />,
    label: 'User Management',
  },
  {
    key: '/dashboard/admin/devices',
    icon: <SettingOutlined />,
    label: 'Device Management',
  },
];

// ─── Page title map ───────────────────────────────────────────────────────────

const PAGE_TITLES: Record<string, string> = {
  '/dashboard': 'Overview',
  '/dashboard/devices': 'Radar Devices',
  '/dashboard/logs': 'Detection Logs',
  '/dashboard/alerts': 'Live Alerts',
  '/dashboard/admin/users': 'User Management',
  '/dashboard/admin/devices': 'Device Management',
};

// ─── DashboardLayout ──────────────────────────────────────────────────────────

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const pathname = usePathname();
  const router = useRouter();
  const { user, isLoading, logout } = useAuth();

  const navItems = [
    ...MONITOR_NAV_ITEMS,
    ...(user?.role === 'Admin' ? ADMIN_ONLY_NAV_ITEMS : []),
  ];

  const pageTitle = PAGE_TITLES[pathname] ?? 'Dashboard';

  function handleNavClick({ key }: { key: string }) {
    router.push(key);
  }

  async function handleLogout() {
    await logout();
  }

  if (isLoading) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '100vh',
          background: 'var(--uav-bg-base)',
        }}
      >
        <Spin size="large" />
      </div>
    );
  }

  return (
    <Layout className={styles.shell} hasSider>
      {/* ── Sidebar ── */}
      <Sider
        className={styles.sider}
        width={220}
        collapsedWidth={64}
        collapsed={collapsed}
        theme="dark"
      >
        {/* Logo block */}
        <div className={styles.siderLogo}>
          <div className={styles.siderLogoIcon}>
            <RadarChartOutlined />
          </div>
          {!collapsed && (
            <div className={styles.siderLogoText}>
              <span className={styles.siderLogoTitle}>UAV Monitor</span>
              <span className={styles.siderLogoSub}>Detection System</span>
            </div>
          )}
        </div>

        {/* Navigation */}
        <Menu
          className={styles.siderMenu}
          theme="dark"
          mode="inline"
          selectedKeys={[pathname]}
          items={navItems}
          onClick={handleNavClick}
        />

        {/* Version badge */}
        {!collapsed && (
          <div className={styles.siderBottom}>
            <div className={styles.versionBadge}>v0.1.0 · ALPHA</div>
          </div>
        )}
      </Sider>

      {/* ── Main ── */}
      <Layout
        className={styles.mainLayout}
        style={{ marginLeft: collapsed ? 64 : 220, transition: 'margin-left 0.2s' }}
      >
        {/* Header */}
        <Header className={styles.header}>
          <div className={styles.headerLeft}>
            <Button
              id="sidebar-toggle"
              type="text"
              size="small"
              icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
              onClick={() => setCollapsed(!collapsed)}
              style={{ color: 'var(--uav-text-secondary)' }}
            />
            <span className={styles.pageTitle}>{pageTitle}</span>
          </div>

          <div className={styles.headerRight}>
            {/* System status */}
            <span className={styles.systemStatus}>
              <span className={styles.systemStatusDot} />
              LIVE
            </span>

            {/* Role tag */}
            <Tag
              color={user?.role === 'Admin' ? 'cyan' : 'blue'}
              style={{ margin: 0, fontWeight: 600, letterSpacing: '0.06em' }}
            >
              {user?.role?.toUpperCase() ?? '—'}
            </Tag>

            {/* User info */}
            {user && (
              <div className={styles.userTag}>
                <UserOutlined style={{ color: 'var(--uav-text-secondary)', fontSize: 12 }} />
                <span className={styles.userName}>
                  {user.email ?? user.id?.slice(0, 8) ?? 'Unknown'}
                </span>
              </div>
            )}

            {/* Logout */}
            <Tooltip title="Sign out">
              <Button
                id="logout-button"
                type="text"
                icon={<LogoutOutlined />}
                onClick={handleLogout}
                style={{ color: 'var(--uav-text-secondary)' }}
                danger
              />
            </Tooltip>
          </div>
        </Header>

        {/* Content */}
        <Content className={styles.content}>{children}</Content>
      </Layout>
    </Layout>
  );
}
