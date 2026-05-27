'use client';

import React, { useState } from 'react';
import { Card, Form, Input, Button, Alert, Typography } from 'antd';
import {
  UserOutlined,
  LockOutlined,
  RadarChartOutlined,
  EyeInvisibleOutlined,
  EyeTwoTone,
} from '@ant-design/icons';
import { useRouter } from 'next/navigation';
import styles from './page.module.css';

const { Text } = Typography;

interface LoginFormValues {
  email: string;
  password: string;
}

/**
 * Login Page
 *
 * Public route — no authentication required.
 * Submits credentials to /api/auth/login (Next.js server-side proxy).
 * On success, the server sets the uav_token HttpOnly cookie and returns { user }.
 * The page then redirects to /dashboard.
 */
export default function LoginPage() {
  const router = useRouter();
  const [form] = Form.useForm<LoginFormValues>();
  const [loading, setLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  async function handleSubmit(values: LoginFormValues) {
    setLoading(true);
    setErrorMessage(null);

    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: values.email, password: values.password }),
      });

      if (!res.ok) {
        let message = 'Authentication failed. Please check your credentials.';
        try {
          const data = await res.json();
          message = data.message ?? message;
        } catch { /* non-JSON body */ }
        setErrorMessage(message);
        return;
      }

      // Cookie is set server-side — redirect to dashboard
      router.push('/dashboard');
      router.refresh();
    } catch {
      setErrorMessage('Unable to connect to the authentication service. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className={styles.root}>
      {/* Animated radar grid background */}
      <div className={styles.background} aria-hidden="true" />

      <Card className={styles.card} variant="borderless">
        {/* Logo & Branding */}
        <div className={styles.logo}>
          <div className={styles.logoIcon}>
            <RadarChartOutlined />
          </div>
          <div className={styles.logoTitle}>UAV Detection</div>
          <div className={styles.logoSubtitle}>Drone Monitoring System</div>
        </div>

        {/* System online indicator */}
        <div style={{ display: 'flex', justifyContent: 'center' }}>
          <span className={styles.statusBadge}>
            <span className={styles.statusDot} />
            SYSTEM ONLINE
          </span>
        </div>

        {/* Error Alert */}
        {errorMessage && (
          <Alert
            className={styles.errorAlert}
            type="error"
            message={errorMessage}
            showIcon
            closable
            onClose={() => setErrorMessage(null)}
          />
        )}

        {/* Login Form */}
        <Form
          form={form}
          layout="vertical"
          onFinish={handleSubmit}
          requiredMark={false}
          autoComplete="off"
          size="large"
        >
          <Form.Item
            name="email"
            label="Email Address"
            rules={[
              { required: true, message: 'Email is required.' },
              { type: 'email', message: 'Please enter a valid email address.' },
            ]}
          >
            <Input
              id="login-email"
              prefix={<UserOutlined style={{ color: 'var(--uav-text-muted)' }} />}
              placeholder="operator@uav-system.io"
              autoComplete="email"
            />
          </Form.Item>

          <Form.Item
            name="password"
            label="Password"
            rules={[
              { required: true, message: 'Password is required.' },
            ]}
          >
            <Input.Password
              id="login-password"
              prefix={<LockOutlined style={{ color: 'var(--uav-text-muted)' }} />}
              placeholder="••••••••"
              autoComplete="current-password"
              iconRender={(visible) =>
                visible ? <EyeTwoTone twoToneColor="#00D4FF" /> : <EyeInvisibleOutlined />
              }
            />
          </Form.Item>

          <Form.Item style={{ marginBottom: 0, marginTop: 8 }}>
            <Button
              id="login-submit"
              type="primary"
              htmlType="submit"
              loading={loading}
              block
              size="large"
              style={{ height: 48, fontWeight: 600, letterSpacing: '0.06em' }}
            >
              {loading ? 'AUTHENTICATING...' : 'AUTHENTICATE'}
            </Button>
          </Form.Item>
        </Form>

        {/* Footer */}
        <div className={styles.footer}>
          <Text type="secondary">
            Authorized personnel only &mdash; all access is logged
          </Text>
        </div>
      </Card>
    </div>
  );
}
