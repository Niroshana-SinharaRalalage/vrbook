'use client';

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { apiFetch } from '../api/client';

interface NegotiateResponse {
  readonly url: string;
  readonly accessToken: string;
  readonly expiresAt: string;
}

let connection: HubConnection | null = null;
let startPromise: Promise<void> | null = null;

/**
 * Slice 7 — singleton SignalR HubConnection. Returns the same instance
 * for the lifetime of the browser tab so React Strict Mode's dev
 * double-invoke of `useEffect` doesn't build two parallel connections.
 *
 * `start()` is idempotent — calling it on an already-`Connected` or
 * `Connecting` instance is a no-op (we cache the in-flight promise).
 *
 * The hook layer (`useTentativeBookingPush`) handles
 * `visibilitychange` pause / resume.
 */
export const getRealtimeConnection = (): HubConnection | null => {
  if (typeof window === 'undefined') {
    return null; // SSR — never instantiate
  }
  if (connection) return connection;

  connection = new HubConnectionBuilder()
    .withUrl('/api/v1/realtime/negotiate', {
      // The negotiate endpoint returns { url, accessToken, expiresAt };
      // we feed `accessToken` to the SDK so it can attach the JWT to
      // the WebSocket handshake.
      accessTokenFactory: async () => {
        const r = await apiFetch<NegotiateResponse>('/api/v1/realtime/negotiate');
        return r.accessToken;
      },
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  return connection;
};

/**
 * Idempotent start — returns the in-flight promise if a `start()` is
 * already running, or resolves immediately if already Connected.
 */
export const startConnection = async (): Promise<void> => {
  const c = getRealtimeConnection();
  if (!c) return;
  if (c.state === HubConnectionState.Connected) return;
  if (startPromise) return startPromise;
  startPromise = c
    .start()
    .catch((err) => {
      // Don't blow up the React tree; the hook reports this as
      // `connected: false` and the dashboard falls back to polling.
      // eslint-disable-next-line no-console
      console.warn('SignalR connection failed', err);
    })
    .finally(() => {
      startPromise = null;
    });
  return startPromise;
};

export const stopConnection = async (): Promise<void> => {
  if (!connection) return;
  if (connection.state === HubConnectionState.Disconnected) return;
  await connection.stop().catch(() => {
    /* swallow; we're tearing down */
  });
};
