'use client';

import React from 'react';
import { Tag } from 'antd';
import type { DeviceStatus } from '@/types/api';

interface StatusBadgeProps {
  status: DeviceStatus;
  showDot?: boolean;
  size?: 'small' | 'default';
}

const STATUS_CONFIG: Record<
  DeviceStatus,
  { color: string; label: string; cssVar: string; animClass: string }
> = {
  Online: {
    color: 'success',
    label: 'ONLINE',
    cssVar: 'var(--uav-status-online)',
    animClass: 'animate-pulse-online',
  },
  Offline: {
    color: 'error',
    label: 'OFFLINE',
    cssVar: 'var(--uav-status-offline)',
    animClass: '',
  },
  Error: {
    color: 'warning',
    label: 'ERROR',
    cssVar: 'var(--uav-status-error)',
    animClass: 'animate-pulse-warning',
  },
};

/**
 * StatusBadge
 *
 * Renders a colored Ant Design Tag with an animated dot indicating device status.
 * Animation classes (animate-pulse-online, animate-pulse-warning) are defined
 * in globals.css and map to DeviceStatus enum values from claude.md §4.
 */
export function StatusBadge({ status, showDot = true, size = 'default' }: StatusBadgeProps) {
  const config = STATUS_CONFIG[status] ?? STATUS_CONFIG.Offline;

  return (
    <Tag
      color={config.color}
      style={{
        margin: 0,
        fontWeight: 600,
        letterSpacing: '0.06em',
        fontSize: size === 'small' ? 10 : 11,
        display: 'inline-flex',
        alignItems: 'center',
        gap: 5,
      }}
    >
      {showDot && (
        <span
          className={config.animClass}
          style={{
            display: 'inline-block',
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: config.cssVar,
            flexShrink: 0,
          }}
        />
      )}
      {config.label}
    </Tag>
  );
}

/**
 * DetectionBadge
 *
 * Indicates whether a radar log entry recorded a drone detection.
 */
export function DetectionBadge({ detected }: { detected: boolean }) {
  return (
    <Tag
      color={detected ? 'error' : 'success'}
      style={{
        margin: 0,
        fontWeight: 600,
        letterSpacing: '0.06em',
        fontSize: 11,
        display: 'inline-flex',
        alignItems: 'center',
        gap: 5,
      }}
    >
      {detected && (
        <span
          className="animate-pulse-alert"
          style={{
            display: 'inline-block',
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: 'var(--uav-status-offline)',
            flexShrink: 0,
          }}
        />
      )}
      {detected ? 'DETECTED' : 'CLEAR'}
    </Tag>
  );
}
