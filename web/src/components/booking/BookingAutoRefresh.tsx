'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

interface BookingAutoRefreshProps {
  /**
   * The current booking status. While the status is in a non-terminal state
   * (Tentative / Confirmed / CheckedIn / Disputed) the page re-fetches every
   * <code>intervalMs</code> so the guest sees the owner's Confirm / Reject /
   * Check-in transitions without a manual refresh. Replaced by SignalR push
   * in Slice 7.
   */
  readonly status: string;
  readonly intervalMs?: number;
}

const NON_TERMINAL = new Set([
  'Draft',
  'Tentative',
  'Confirmed',
  'CheckedIn',
  'Disputed',
]);

export const BookingAutoRefresh = ({ status, intervalMs = 5000 }: BookingAutoRefreshProps) => {
  const router = useRouter();

  useEffect(() => {
    if (!NON_TERMINAL.has(status)) return undefined;
    const t = setInterval(() => {
      router.refresh();
    }, intervalMs);
    return () => clearInterval(t);
  }, [status, intervalMs, router]);

  return null;
};
