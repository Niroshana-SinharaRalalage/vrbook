'use client';

import { useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';

import { track } from '@/lib/analytics/analytics';

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

  // VRB-311 funnel — fire once when the booking reaches Confirmed.
  const confirmedTracked = useRef(false);
  useEffect(() => {
    if (status === 'Confirmed' && !confirmedTracked.current) {
      confirmedTracked.current = true;
      track('booking_confirmed');
    }
  }, [status]);

  useEffect(() => {
    if (!NON_TERMINAL.has(status)) return undefined;
    const t = setInterval(() => {
      router.refresh();
    }, intervalMs);
    return () => clearInterval(t);
  }, [status, intervalMs, router]);

  return null;
};
