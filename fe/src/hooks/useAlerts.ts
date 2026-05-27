'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type { DroneDetectionEvent, DeviceStatusChangedEvent } from '@/types/api';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface AlertEntry {
  id: string;          // client-generated for React key
  type: 'detection' | 'status';
  receivedAt: string;  // ISO timestamp (client clock)
  detection?: DroneDetectionEvent;
  statusChange?: DeviceStatusChangedEvent;
}

/**
 * useAlerts — manages a @microsoft/signalr connection to the AlertService hub.
 *
 * Hub endpoint: /ws/alerts (claude.md §7 — AlertService)
 * Auth: JWT passed as ?access_token=... query param (standard SignalR WS auth pattern)
 * Token source: GET /api/auth/token (reads HttpOnly cookie server-side)
 *
 * SignalR events handled:
 *   "DroneDetected"        → DroneDetectionEvent (from q.alert.realtime via RabbitMQ)
 *   "DeviceStatusChanged"  → DeviceStatusChangedEvent (from q.status.changes)
 */
export function useAlerts() {
  const [status, setStatus] = useState<ConnectionStatus>('disconnected');
  const [alerts, setAlerts] = useState<AlertEntry[]>([]);
  const connectionRef = useRef<HubConnection | null>(null);

  const wsUrl = process.env.NEXT_PUBLIC_WS_URL ?? 'http://localhost:80';

  const addAlert = useCallback((entry: Omit<AlertEntry, 'id' | 'receivedAt'>) => {
    setAlerts((prev) => [
      {
        ...entry,
        id: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
        receivedAt: new Date().toISOString(),
      },
      ...prev.slice(0, 99), // Keep at most 100 alerts in memory
    ]);
  }, []);

  const connect = useCallback(async () => {
    // Prevent double-connect
    if (connectionRef.current?.state === HubConnectionState.Connected) return;

    setStatus('connecting');

    // ── Fetch JWT token (for SignalR accessTokenFactory) ──
    let token: string;
    try {
      const res = await fetch('/api/auth/token');
      if (!res.ok) { setStatus('disconnected'); return; }
      const data = await res.json();
      token = data.token;
    } catch {
      setStatus('disconnected');
      return;
    }

    // ── Fetch userId (required by AlertHub group isolation) ──
    // AlertHub.OnConnectedAsync reads ?userId=<guid> from Query and aborts if missing.
    // Source: AlertHub.cs line 32-44
    let userId: string;
    try {
      const res = await fetch('/api/auth/me');
      if (!res.ok) { setStatus('disconnected'); return; }
      const data = await res.json();
      userId = data.id; // /api/auth/me returns { id, role, email }
    } catch {
      setStatus('disconnected');
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${wsUrl}/ws/alerts?userId=${userId}`, {
        // Pass token as query param — standard SignalR WS auth (claude.md §7)
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(
        process.env.NODE_ENV === 'development' ? LogLevel.Information : LogLevel.Error
      )
      .build();

    // ── Event handlers ──
    connection.on('DroneDetected', (payload: DroneDetectionEvent) => {
      addAlert({ type: 'detection', detection: payload });
    });

    connection.on('DeviceStatusChanged', (payload: DeviceStatusChangedEvent) => {
      addAlert({ type: 'status', statusChange: payload });
    });

    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => setStatus('connected'));
    connection.onclose(() => setStatus('disconnected'));

    connectionRef.current = connection;

    try {
      await connection.start();
      setStatus('connected');
    } catch {
      setStatus('disconnected');
    }
  }, [wsUrl, addAlert]);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
    }
    setStatus('disconnected');
  }, []);

  const clearAlerts = useCallback(() => setAlerts([]), []);

  // Auto-connect on mount, disconnect on unmount
  useEffect(() => {
    connect();
    return () => {
      connectionRef.current?.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return {
    status,
    alerts,
    detectionCount: alerts.filter((a) => a.type === 'detection').length,
    connect,
    disconnect,
    clearAlerts,
  };
}
