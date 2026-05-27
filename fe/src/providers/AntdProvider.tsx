'use client';

import React, { useState } from 'react';
import { ConfigProvider, theme } from 'antd';
import { StyleProvider, createCache } from '@ant-design/cssinjs';
import type { ThemeConfig } from 'antd';

/**
 * Industrial Dark Theme Token Specification
 * Source of Truth: claude.md §2 (UAV Drone Detection System)
 *
 * Palette rationale:
 *  - colorPrimary   (#00D4FF) — Cyan radar sweep / signal acquisition aesthetic
 *  - colorBgBase    (#0A0D14) — Near-black control room background
 *  - colorSuccess   (#22C55E) — DeviceStatus.Online
 *  - colorError     (#EF4444) — DeviceStatus.Offline / critical detection badge
 *  - colorWarning   (#F59E0B) — DeviceStatus.Error / degraded alert
 */
const UAV_DARK_THEME: ThemeConfig = {
  algorithm: theme.darkAlgorithm,
  token: {
    // ── Brand / Primary ───────────────────────────────────────────
    colorPrimary: '#00D4FF',
    colorLink: '#00D4FF',

    // ── Background layers ─────────────────────────────────────────
    colorBgBase: '#0A0D14',
    colorBgContainer: '#111827',
    colorBgElevated: '#1C2333',
    colorBgLayout: '#0A0D14',
    colorBgSpotlight: '#1C2333',

    // ── Borders ───────────────────────────────────────────────────
    colorBorder: '#1F2D40',
    colorBorderSecondary: '#1A2535',

    // ── Text ──────────────────────────────────────────────────────
    colorText: '#E2E8F0',
    colorTextSecondary: '#94A3B8',
    colorTextTertiary: '#64748B',
    colorTextQuaternary: '#334155',

    // ── Semantic status colours ───────────────────────────────────
    colorSuccess: '#22C55E',  // DeviceStatus.Online
    colorWarning: '#F59E0B',  // DeviceStatus.Error
    colorError: '#EF4444',    // DeviceStatus.Offline / DroneDetected badge
    colorInfo: '#3B82F6',     // Monitor role accent / informational

    // ── Typography ────────────────────────────────────────────────
    fontFamily: "'JetBrains Mono', 'Courier New', monospace",
    fontSize: 14,
    fontSizeHeading1: 28,
    fontSizeHeading2: 22,
    fontSizeHeading3: 18,
    fontSizeHeading4: 16,

    // ── Shape ─────────────────────────────────────────────────────
    borderRadius: 6,
    borderRadiusLG: 8,
    borderRadiusSM: 4,
    wireframe: false,

    // ── Motion ────────────────────────────────────────────────────
    motionDurationFast: '0.1s',
    motionDurationMid: '0.2s',
    motionDurationSlow: '0.3s',

    // ── Shadow ────────────────────────────────────────────────────
    boxShadow: '0 1px 3px 0 rgba(0,0,0,0.5), 0 1px 2px -1px rgba(0,0,0,0.5)',
    boxShadowSecondary: '0 4px 6px -1px rgba(0,0,0,0.4)',
  },
  components: {
    // ── Layout ────────────────────────────────────────────────────
    Layout: {
      siderBg: '#0D1117',
      headerBg: '#0D1117',
      bodyBg: '#0A0D14',
      triggerBg: '#1C2333',
    },
    // ── Menu (sidebar navigation) ─────────────────────────────────
    Menu: {
      darkItemBg: '#0D1117',
      darkSubMenuItemBg: '#0A0D14',
      darkItemSelectedBg: 'rgba(0, 212, 255, 0.12)',
      darkItemSelectedColor: '#00D4FF',
      darkItemHoverBg: 'rgba(0, 212, 255, 0.06)',
      darkItemHoverColor: '#E2E8F0',
      itemHeight: 44,
    },
    // ── Table (radar log display) ─────────────────────────────────
    Table: {
      headerBg: '#0D1117',
      rowHoverBg: 'rgba(0, 212, 255, 0.04)',
      borderColor: '#1F2D40',
      headerSortActiveBg: '#111827',
    },
    // ── Card (device panels) ──────────────────────────────────────
    Card: {
      headerBg: '#111827',
      extraColor: '#94A3B8',
    },
    // ── Button ────────────────────────────────────────────────────
    Button: {
      primaryShadow: '0 0 12px rgba(0, 212, 255, 0.3)',
    },
    // ── Input / Form ─────────────────────────────────────────────
    Input: {
      activeBorderColor: '#00D4FF',
      hoverBorderColor: '#38BDF8',
      activeShadow: '0 0 0 2px rgba(0, 212, 255, 0.2)',
    },
    // ── Badge (status indicators) ────────────────────────────────
    Badge: {
      statusSize: 8,
    },
    // ── Statistic (KPI tiles) ─────────────────────────────────────
    Statistic: {
      titleFontSize: 12,
      contentFontSize: 28,
    },
    // ── Tag (drone type / status chips) ──────────────────────────
    Tag: {
      defaultBg: '#1C2333',
      defaultColor: '#94A3B8',
    },
    // ── Alert (notification banners) ─────────────────────────────
    Alert: {
      colorInfoBg: 'rgba(59, 130, 246, 0.08)',
      colorInfoBorder: 'rgba(59, 130, 246, 0.3)',
      colorSuccessBg: 'rgba(34, 197, 94, 0.08)',
      colorSuccessBorder: 'rgba(34, 197, 94, 0.3)',
      colorWarningBg: 'rgba(245, 158, 11, 0.08)',
      colorWarningBorder: 'rgba(245, 158, 11, 0.3)',
      colorErrorBg: 'rgba(239, 68, 68, 0.08)',
      colorErrorBorder: 'rgba(239, 68, 68, 0.3)',
    },
  },
};

interface AntdProviderProps {
  children: React.ReactNode;
}

/**
 * AntdProvider
 *
 * Wraps the application with:
 *  1. StyleProvider — @ant-design/cssinjs cache for SSR-safe style injection
 *     (required by Next.js App Router to prevent style flickering / FOUC)
 *  2. ConfigProvider — Ant Design 5.x Industrial Dark theme tokens
 *
 * Must be rendered as a Client Component ('use client') and placed in
 * the root layout as high as possible in the component tree.
 */
export function AntdProvider({ children }: AntdProviderProps) {
  const [cache] = useState(() => createCache());

  return (
    <StyleProvider cache={cache}>
      <ConfigProvider theme={UAV_DARK_THEME}>
        {children}
      </ConfigProvider>
    </StyleProvider>
  );
}

export default AntdProvider;
