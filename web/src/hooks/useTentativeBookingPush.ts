'use client';

import { useEffect, useState } from 'react';
import { HubConnectionState } from '@microsoft/signalr';
import {
  peekRealtimeConnection,
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
 * so the dashboard can render a Live indicator and decide whether to
 * fall back to visibility-refetch polling.
 */
export const useTentativeBookingPush = (
  onPushed: (payload: TentativeAddedPayload) => void,
): { connected: boolean } => {
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (typeof window === 'undefined') return;

    let cancelled = false;
    let attachedConnection: ReturnType<typeof peekRealtimeConnection> | null = null;

    const handler = (payload: TentativeAddedPayload) => {
      onPushed(payload);
    };

    const wireHandlers = () => {
      const c = peekRealtimeConnection();
      if (!c || c === attachedConnection) return;
      attachedConnection = c;
      c.on('tentativeBookingAdded', handler);
      c.onreconnected(() => setConnected(true));
      c.onreconnecting(() => setConnected(false));
      c.onclose(() => setConnected(false));
    };

    void (async () => {
      if (document.visibilityState === 'hidden') {
        return; // don't connect while backgrounded
      }
      const conn = await startConnection();
      if (cancelled) return;
      wireHandlers();
      setConnected(conn?.state === HubConnectionState.Connected);
    })();

    const onVisibility = () => {
      if (document.visibilityState === 'hidden') {
        void stopConnection().then(() => {
          if (!cancelled) setConnected(false);
        });
      } else {
        void startConnection().then((conn) => {
          if (cancelled) return;
          wireHandlers();
          setConnected(conn?.state === HubConnectionState.Connected);
        });
      }
    };
    document.addEventListener('visibilitychange', onVisibility);

    return () => {
      cancelled = true;
      if (attachedConnection) {
        attachedConnection.off('tentativeBookingAdded', handler);
      }
      document.removeEventListener('visibilitychange', onVisibility);
      // We intentionally do NOT stop the singleton on unmount — keeping it
      // alive across SPA navigations avoids reconnect churn.
    };
  }, [onPushed]);

  return { connected };
};
