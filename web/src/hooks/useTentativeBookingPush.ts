'use client';

import { useEffect, useState } from 'react';
import { HubConnectionState } from '@microsoft/signalr';
import {
  getRealtimeConnection,
  startConnection,
  stopConnection,
} from '@/lib/realtime/connection';

export interface TentativeAddedPayload {
  readonly bookingId: string;
  readonly reference: string;
  readonly checkinDate: string;
  readonly checkoutDate: string;
  readonly tentativeUntil: string;
}

/**
 * Slice 7 — subscribes to the `tentativeBookingAdded` SignalR event and
 * pauses the connection while the tab is hidden. Returns `{ connected }`
 * so the dashboard can render a "Live" indicator and decide whether to
 * fall back to visibility-refetch polling.
 */
export const useTentativeBookingPush = (
  onPushed: (payload: TentativeAddedPayload) => void,
): { connected: boolean } => {
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    const c = getRealtimeConnection();
    if (!c) return; // SSR

    const handler = (payload: TentativeAddedPayload) => {
      onPushed(payload);
    };
    c.on('tentativeBookingAdded', handler);

    const onStateChange = () => {
      setConnected(c.state === HubConnectionState.Connected);
    };
    c.onreconnected(onStateChange);
    c.onreconnecting(onStateChange);
    c.onclose(onStateChange);

    let cancelled = false;
    void (async () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        return; // don't connect while backgrounded
      }
      await startConnection();
      if (!cancelled) {
        setConnected(c.state === HubConnectionState.Connected);
      }
    })();

    const onVisibility = () => {
      if (document.visibilityState === 'hidden') {
        void stopConnection().then(() => setConnected(false));
      } else {
        void startConnection().then(() => {
          setConnected(c.state === HubConnectionState.Connected);
        });
      }
    };
    document.addEventListener('visibilitychange', onVisibility);

    return () => {
      cancelled = true;
      c.off('tentativeBookingAdded', handler);
      document.removeEventListener('visibilitychange', onVisibility);
      // We intentionally do NOT stop the connection on unmount — keeping
      // the singleton alive across SPA navigations avoids reconnect
      // churn. Page close terminates the WebSocket naturally.
    };
  }, [onPushed]);

  return { connected };
};
