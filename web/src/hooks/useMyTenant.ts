/**
 * Slice OPS.M.7 §3.2 + §3.7 — `useMyTenant()` hook backing the onboarding
 * wizard's polling loop.
 *
 * Behavior:
 *   - One-shot fetch by default (pollIntervalMs undefined).
 *   - Polling enabled when caller passes `pollIntervalMs` (e.g. 1000ms).
 *   - Polling stops automatically when `stopWhen(data) === true` (typically
 *     `t => t.onboarding.isComplete`) — saves API requests once Stripe
 *     readiness flips.
 *   - Hard cap at `pollMax` attempts (default 30) so a stuck Stripe state
 *     doesn't poll forever. Exposes `pollAttempts` + `isExhausted` so the
 *     wizard can show a "Refresh now" fallback per §3.7 (D7).
 */
import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getMyTenant, type MeTenant } from '@/lib/api/tenant';

export interface UseMyTenantOptions {
  readonly pollIntervalMs?: number;
  readonly pollMax?: number;
  readonly stopWhen?: (t: MeTenant) => boolean;
}

export interface UseMyTenantResult {
  readonly data: MeTenant | undefined;
  readonly isLoading: boolean;
  readonly isError: boolean;
  readonly error: Error | null;
  readonly pollAttempts: number;
  readonly isExhausted: boolean;
  readonly refetch: () => Promise<unknown>;
}

const DEFAULT_POLL_MAX = 30;

export const useMyTenant = (opts: UseMyTenantOptions = {}): UseMyTenantResult => {
  const pollMax = opts.pollMax ?? DEFAULT_POLL_MAX;
  const [attempts, setAttempts] = useState(0);
  const [exhausted, setExhausted] = useState(false);
  const exhaustedRef = useRef(false);
  exhaustedRef.current = exhausted;

  const query = useQuery({
    queryKey: ['me', 'tenant'],
    queryFn: getMyTenant,
    staleTime: 0,
    refetchInterval: (q) => {
      if (opts.pollIntervalMs === undefined) return false;
      if (exhaustedRef.current) return false;
      const data = q.state.data as MeTenant | undefined;
      if (data && opts.stopWhen?.(data)) return false;
      return opts.pollIntervalMs;
    },
  });

  // Count successful refetches as poll attempts; flip `exhausted` when
  // we hit the cap. The hook still returns the latest data so the UI can
  // show a "Refresh now" CTA without losing context.
  useEffect(() => {
    if (opts.pollIntervalMs === undefined) return;
    if (query.isFetching) return;
    if (!query.data) return;
    setAttempts((n) => {
      const next = n + 1;
      if (next >= pollMax) setExhausted(true);
      return next;
    });
    // We intentionally watch `query.dataUpdatedAt` rather than `query.data`
    // — react-query mutates the same object reference on refetch when the
    // payload is identical, so dataUpdatedAt is the more reliable tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.dataUpdatedAt]);

  return {
    data: query.data,
    isLoading: query.isPending,
    isError: query.isError,
    error: query.error,
    pollAttempts: attempts,
    isExhausted: exhausted,
    refetch: query.refetch,
  };
};
