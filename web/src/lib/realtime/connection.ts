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

// Singleton state. typed as | null so SSR-safe access is explicit.
let cachedConnection: HubConnection | null = null;
let buildPromise: Promise<HubConnection> | null = null;
let startPromise: Promise<void> | null = null;

/**
 * Slice 7 — manually-negotiated SignalR connection. We DO NOT use the SDK's
 * auto-negotiate (`.withUrl('/our-negotiate-path')`) because that double-POSTs
 * to `/our-negotiate-path/negotiate?negotiateVersion=1`. Instead we fetch
 * negotiate ourselves and pass the Service URL straight to `.withUrl(...)`.
 *
 * This is the documented Azure SignalR Serverless client pattern; see also
 * `docs/SLICE7_PLAN.md` §2.7.
 */
const buildConnection = async (): Promise<HubConnection> => {
  if (cachedConnection) return cachedConnection;
  if (buildPromise) return buildPromise;

  buildPromise = (async () => {
    const initial = await apiFetch<NegotiateResponse>('/api/v1/realtime/negotiate');

    const conn = new HubConnectionBuilder()
      .withUrl(initial.url, {
        // The SDK calls this on every reconnect attempt; we re-negotiate with
        // our API to get a fresh token (the 1h TTL is invisible to the user).
        accessTokenFactory: async () => {
          const fresh = await apiFetch<NegotiateResponse>('/api/v1/realtime/negotiate');
          return fresh.accessToken;
        },
        // We negotiated ourselves; tell the SDK not to do it again.
        skipNegotiation: true,
        transport: 1, // WebSockets only (HttpTransportType.WebSockets)
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    cachedConnection = conn;
    return conn;
  })();

  try {
    return await buildPromise;
  } catch (err) {
    // Don't poison the cache; next caller can retry.
    buildPromise = null;
    // eslint-disable-next-line no-console
    console.warn('SignalR build failed', err);
    throw err;
  }
};

/**
 * Returns the cached connection if one exists. Used by the hook to wire
 * `.on(...)` handlers; safe on SSR because it returns null instead of building.
 */
export const peekRealtimeConnection = (): HubConnection | null =>
  typeof window === 'undefined' ? null : cachedConnection;

/**
 * Idempotent start. Resolves after the connection enters Connected state, or
 * silently swallows the error and leaves the connection Disconnected if the
 * Service / negotiate is unreachable.
 */
export const startConnection = async (): Promise<HubConnection | null> => {
  if (typeof window === 'undefined') return null;

  if (startPromise) {
    await startPromise;
    return cachedConnection;
  }

  startPromise = (async () => {
    try {
      const conn = await buildConnection();
      if (conn.state === HubConnectionState.Connected) return;
      if (conn.state === HubConnectionState.Connecting) return;
      if (conn.state === HubConnectionState.Reconnecting) return;
      await conn.start();
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn('SignalR start failed', err);
    }
  })().finally(() => {
    startPromise = null;
  });

  await startPromise;
  return cachedConnection;
};

export const stopConnection = async (): Promise<void> => {
  if (!cachedConnection) return;
  if (cachedConnection.state === HubConnectionState.Disconnected) return;
  await cachedConnection.stop().catch(() => {
    /* swallow; we're tearing down */
  });
};
