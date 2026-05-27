'use client';

import React from 'react';
import { Card, Button, Typography, Tag, Empty } from 'antd';
import {
  AlertOutlined, WifiOutlined, DisconnectOutlined,
  CloseCircleOutlined, LoadingOutlined,
} from '@ant-design/icons';
import { useAlerts, type AlertEntry, type ConnectionStatus } from '@/hooks/useAlerts';
import styles from './page.module.css';

const { Text, Title } = Typography;

function ConnectionBar({ status }: { status: ConnectionStatus }) {
  const configs: Record<ConnectionStatus, { dotClass: string; label: string; color: string; icon: React.ReactNode }> = {
    connected: { dotClass: styles.connectionDotConnected, label: 'Connected to alert stream', color: 'var(--uav-status-online)', icon: <WifiOutlined /> },
    connecting: { dotClass: styles.connectionDotReconnecting, label: 'Connecting to alert stream...', color: 'var(--uav-status-error)', icon: <LoadingOutlined spin /> },
    reconnecting: { dotClass: styles.connectionDotReconnecting, label: 'Reconnecting...', color: 'var(--uav-status-error)', icon: <LoadingOutlined spin /> },
    disconnected: { dotClass: styles.connectionDotDisconnected, label: 'Disconnected', color: 'var(--uav-text-muted)', icon: <DisconnectOutlined /> },
  };
  const config = configs[status];
  return (
    <div className={styles.connectionBar}>
      <span className={`${styles.connectionDot} ${config.dotClass}`} />
      <span style={{ color: config.color, fontSize: 12 }}>{config.icon}</span>
      <span className={styles.connectionText}>{config.label}</span>
    </div>
  );
}

function AccuracyValue({ accuracy }: { accuracy: number }) {
  const pct = Math.round(accuracy * 100);
  const cls = pct >= 90 ? styles.accuracyHigh : pct >= 70 ? styles.accuracyMed : styles.accuracyLow;
  return <span className={`${styles.alertFieldValue} ${cls}`}>{pct}%</span>;
}

function AlertCard({ entry }: { entry: AlertEntry }) {
  const isDetection = entry.type === 'detection';
  const cardClass = `${styles.alertCard} ${isDetection ? styles.alertCardDetection : styles.alertCardStatus}`;

  return (
    <Card size="small" className={cardClass}>
      <div className={styles.alertHeader}>
        {isDetection ? (
          <Tag color="error" style={{ margin: 0, fontWeight: 700, letterSpacing: '0.06em', fontSize: 11 }}>
            <AlertOutlined /> DRONE DETECTED
          </Tag>
        ) : (
          <Tag color="cyan" style={{ margin: 0, fontWeight: 700, letterSpacing: '0.06em', fontSize: 11 }}>
            STATUS CHANGE
          </Tag>
        )}
        <span className={styles.alertTimestamp}>
          {new Date(entry.receivedAt).toLocaleTimeString('en-GB', { timeStyle: 'medium' })}
        </span>
      </div>

      {isDetection && entry.detection && (
        <div className={styles.alertBody}>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Device</span>
            <span className={styles.alertFieldValue}>#{entry.detection.deviceId}</span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Location</span>
            <span className={styles.alertFieldValue}>{entry.detection.location}</span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Drone Type</span>
            <span className={styles.alertFieldValue} style={{ color: 'var(--uav-status-offline)' }}>
              {entry.detection.droneType}
            </span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Accuracy</span>
            <AccuracyValue accuracy={entry.detection.accuracy} />
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Control State</span>
            <span className={styles.alertFieldValue}>{entry.detection.controlState}</span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Detected At</span>
            <span className={styles.alertFieldValue} style={{ fontSize: 11, color: 'var(--uav-text-secondary)' }}>
              {new Date(entry.detection.detectedAt).toLocaleTimeString('en-GB', { timeStyle: 'medium' })}
            </span>
          </div>
        </div>
      )}

      {!isDetection && entry.statusChange && (
        <div className={styles.alertBody}>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Device</span>
            <span className={styles.alertFieldValue}>#{entry.statusChange.deviceId}</span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Location</span>
            <span className={styles.alertFieldValue}>{entry.statusChange.location}</span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>Previous</span>
            <span className={styles.alertFieldValue} style={{ color: 'var(--uav-text-muted)' }}>
              {entry.statusChange.previousStatus}
            </span>
          </div>
          <div className={styles.alertField}>
            <span className={styles.alertFieldLabel}>New Status</span>
            <span className={styles.alertFieldValue} style={{
              color: entry.statusChange.newStatus === 'Online' ? 'var(--uav-status-online)' :
                     entry.statusChange.newStatus === 'Offline' ? 'var(--uav-status-offline)' :
                     'var(--uav-status-error)',
            }}>
              {entry.statusChange.newStatus}
            </span>
          </div>
        </div>
      )}
    </Card>
  );
}

export default function AlertsPage() {
  const { status, alerts, detectionCount, connect, clearAlerts } = useAlerts();

  return (
    <div>
      <div style={{ marginBottom: 20, display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <Title level={3} style={{ margin: 0, color: 'var(--uav-text-primary)' }}>Live Alerts</Title>
          <Text type="secondary" style={{ fontSize: 13 }}>
            Real-time drone detection events from the AlertService WebSocket hub.
          </Text>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          {alerts.length > 0 && (
            <Button id="alerts-clear" size="small" icon={<CloseCircleOutlined />} onClick={clearAlerts}>
              Clear ({alerts.length})
            </Button>
          )}
          {status === 'disconnected' && (
            <Button id="alerts-reconnect" size="small" type="primary" icon={<WifiOutlined />} onClick={connect}>
              Reconnect
            </Button>
          )}
        </div>
      </div>

      <ConnectionBar status={status} />

      {detectionCount > 0 && (
        <div style={{ marginBottom: 16 }}>
          <Tag color="error" style={{ fontSize: 12, padding: '4px 12px', fontWeight: 700 }}>
            <AlertOutlined /> {detectionCount} drone detection{detectionCount !== 1 ? 's' : ''} this session
          </Tag>
        </div>
      )}

      <div className={styles.alertFeed}>
        {alerts.length === 0 ? (
          <div className={styles.emptyFeed}>
            <AlertOutlined className={styles.emptyIcon} />
            <Text type="secondary" style={{ fontSize: 13 }}>
              {status === 'connected' ? 'Monitoring for events... All clear.' : 'Connect to start receiving alerts.'}
            </Text>
            <div className={styles.scanLine} />
          </div>
        ) : (
          alerts.map((entry) => <AlertCard key={entry.id} entry={entry} />)
        )}
      </div>
    </div>
  );
}
