'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { Clock, ChevronRight, ClipboardList } from 'lucide-react';

import { adminListBookings, type AdminBookingSummary, type BookingStatus } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';
import { formatCurrency } from '@/lib/utils/currency';

// Slice 2 — owner's booking queue. Tentative-first ordering, status filters,
// click-through to the detail page for Confirm / Reject.
const FILTERS: { label: string; value: BookingStatus | 'All' }[] = [
  { label: 'All', value: 'All' },
  { label: 'Tentative', value: 'Tentative' },
  { label: 'Confirmed', value: 'Confirmed' },
  { label: 'Checked in', value: 'CheckedIn' },
  { label: 'Checked out', value: 'CheckedOut' },
  { label: 'Cancelled', value: 'Cancelled' },
  { label: 'Rejected', value: 'Rejected' },
];

const STATUS_PILL: Record<BookingStatus, string> = {
  Draft: 'bg-muted text-muted-foreground',
  Tentative: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300',
  Confirmed: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300',
  CheckedIn: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300',
  CheckedOut: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300',
  Completed: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300',
  Cancelled: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300',
  Rejected: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300',
  Disputed: 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-300',
  Refunded: 'bg-muted text-muted-foreground',
};

const AdminBookingsPage = () => {
  const [items, setItems] = useState<readonly AdminBookingSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<BookingStatus | 'All'>('All');

  useEffect(() => {
    setLoading(true);
    setError(null);
    (async () => {
      try {
        const list = await adminListBookings(filter === 'All' ? undefined : filter);
        setItems(list);
      } catch (err) {
        setError(
          err instanceof ApiProblemError
            ? err.problem.detail ?? err.message
            : err instanceof Error
              ? err.message
              : 'Failed to load',
        );
      } finally {
        setLoading(false);
      }
    })();
  }, [filter]);

  const tentativeCount = items.filter((b) => b.status === 'Tentative').length;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Bookings</h1>
        {tentativeCount > 0 && (
          <p className="mt-1 text-sm text-yellow-700 dark:text-yellow-300">
            {tentativeCount} {tentativeCount === 1 ? 'booking' : 'bookings'} awaiting your decision.
          </p>
        )}
      </div>

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((f) => (
          <button
            key={f.value}
            onClick={() => setFilter(f.value)}
            className={`rounded-full border px-3 py-1 text-xs ${
              filter === f.value
                ? 'border-brand-maroon-600 bg-brand-maroon-50 text-brand-maroon-700 dark:bg-brand-maroon-900/30 dark:text-brand-orange-200'
                : 'border-border text-muted-foreground hover:border-foreground/30 hover:text-foreground'
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {loading && <p className="text-sm text-muted-foreground">Loading…</p>}

      {error && (
        <div className="rounded-md border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}
        </div>
      )}

      {!loading && !error && items.length === 0 && (
        <div className="rounded-xl border border-dashed border-border p-12 text-center text-sm text-muted-foreground">
          <ClipboardList className="mx-auto h-10 w-10" aria-hidden />
          <p className="mt-3">No bookings {filter !== 'All' && `in status ${filter}`} yet.</p>
        </div>
      )}

      {!loading && !error && items.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-border">
          <table className="w-full text-sm">
            <thead className="border-b border-border bg-muted/30 text-left text-xs uppercase tracking-wider text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Reference</th>
                <th className="px-4 py-3">Property</th>
                <th className="px-4 py-3">Guest</th>
                <th className="px-4 py-3">Dates</th>
                <th className="px-4 py-3">Total</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {items.map((b) => (
                <tr
                  key={b.id}
                  className="border-b border-border last:border-b-0 hover:bg-muted/40"
                >
                  <td className="px-4 py-3 font-mono text-xs">{b.reference}</td>
                  <td className="px-4 py-3">{b.propertyTitle}</td>
                  <td className="px-4 py-3">
                    <div>{b.guestDisplayName}</div>
                    <div className="text-xs text-muted-foreground">{b.guestCount} guests</div>
                  </td>
                  <td className="px-4 py-3">
                    {b.checkinDate} → {b.checkoutDate}
                  </td>
                  <td className="px-4 py-3">{formatCurrency(b.total, b.currency)}</td>
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${STATUS_PILL[b.status]}`}
                    >
                      {b.status === 'Tentative' ? 'Awaiting host' : b.status}
                    </span>
                    {b.status === 'Tentative' && b.tentativeUntil && (
                      <div className="mt-1 inline-flex items-center gap-1 text-xs text-muted-foreground">
                        <Clock className="h-3 w-3" aria-hidden />
                        until {new Date(b.tentativeUntil).toLocaleString()}
                      </div>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Link
                      href={`/admin/bookings/${b.id}`}
                      className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs hover:bg-accent"
                    >
                      Open <ChevronRight className="h-3.5 w-3.5" aria-hidden />
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default AdminBookingsPage;
