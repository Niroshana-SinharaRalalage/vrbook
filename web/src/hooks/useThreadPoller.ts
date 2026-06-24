'use client';

import { useEffect, useRef } from 'react';

/**
 * Slice 6 polling hook — the seam Slice 7 replaces with SignalR.
 *
 * Calls `fetcher` once on mount and again every `intervalMs` (default 30s).
 * Pauses when `document.visibilityState === 'hidden'` so a backgrounded
 * tab doesn't burn the API rate limiter.
 *
 * In Slice 7 swap this hook for `useThreadStream` — `ThreadInbox` and
 * `ConversationPane` only depend on this one hook, so the swap is a
 * single import change per call site.
 */
export const useThreadPoller = (
  fetcher: () => Promise<void>,
  intervalMs = 30_000,
): void => {
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  useEffect(() => {
    let cancelled = false;

    const tick = () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        return;
      }
      if (cancelled) return;
      void fetcherRef.current();
    };

    void fetcherRef.current(); // initial fetch

    const id = window.setInterval(tick, intervalMs);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [intervalMs]);
};
