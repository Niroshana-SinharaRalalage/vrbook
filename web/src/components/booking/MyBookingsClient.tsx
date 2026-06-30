'use client';

import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { ChevronRight, LogIn } from 'lucide-react';

import { useAuth } from '@/lib/auth/useAuth';
import { ApiProblemError } from '@/lib/api/client';
import { myBookings, type BookingSummary } from '@/lib/api/booking';
import { formatCurrency } from '@/lib/utils/currency';

const StatusBadge = ({ status }: { status: BookingSummary['status'] }) => {
  const colorByStatus: Partial<Record<BookingSummary['status'], string>> = {
    Tentative: 'bg-yellow-100 text-yellow-800',
    Confirmed: 'bg-green-100 text-green-800',
    CheckedIn: 'bg-blue-100 text-blue-800',
    CheckedOut: 'bg-blue-100 text-blue-800',
    Completed: 'bg-green-100 text-green-800',
    Cancelled: 'bg-red-100 text-red-800',
    Rejected: 'bg-red-100 text-red-800',
  };
  const cls = colorByStatus[status] ?? 'bg-muted text-muted-foreground';
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs ${cls}`}>{status}</span>
  );
};

const Skeleton = () => (
  <div className="space-y-3">
    {Array.from({ length: 3 }).map((_, i) => (
      <div key={i} className="h-20 animate-pulse rounded-xl bg-muted" />
    ))}
  </div>
);

/**
 * Slice OPS.M.10.2 F11.7.3 — client-rendered My bookings list.
 * Same fix as BookingDetailClient: the page was a server component
 * calling myBookings() server-side without the user's MSAL token.
 * Result: 401 propagated up the SSR pipeline and crashed the page.
 */
export const MyBookingsClient = () => {
  const { isAuthenticated, isBusy, signIn } = useAuth();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['my-bookings'],
    queryFn: () => myBookings(),
    enabled: isAuthenticated && !isBusy,
    retry: false,
  });

  if (!isAuthenticated && !isBusy) {
    return (
      <div className="mx-auto max-w-md py-12 text-center">
        <h1 className="text-xl font-semibold">Sign in to see your bookings</h1>
        <button
          type="button"
          onClick={signIn}
          className="mt-4 inline-flex items-center gap-1.5 rounded-md bg-brand-orange-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-orange-700"
        >
          <LogIn className="h-4 w-4" aria-hidden /> Sign in
        </button>
      </div>
    );
  }

  if (isBusy || isLoading) {
    return <Skeleton />;
  }

  if (isError) {
    const message = error instanceof ApiProblemError
      ? error.problem.detail ?? error.message
      : error instanceof Error
        ? error.message
        : 'Failed to load';
    return (
      <div className="rounded-xl border border-destructive/30 bg-destructive/5 p-6 text-sm text-destructive">
        {message}
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-12 text-center text-sm text-muted-foreground">
        You don&apos;t have any bookings yet.
        <div className="mt-3">
          <Link href="/properties" className="text-foreground underline">
            Browse stays
          </Link>{' '}
          to make your first booking.
        </div>
      </div>
    );
  }

  return (
    <ul className="space-y-3">
      {data.items.map((b) => (
        <li key={b.id}>
          <Link
            href={`/bookings/${b.id}`}
            className="flex items-center gap-4 rounded-xl border border-border bg-card p-4 transition-colors hover:bg-accent"
          >
            <div className="flex-1 space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-medium">{b.propertyTitle}</span>
                <StatusBadge status={b.status} />
              </div>
              <p className="text-xs text-muted-foreground">
                {b.reference} · {b.checkinDate} → {b.checkoutDate}
              </p>
            </div>
            <div className="text-right">
              <p className="text-sm font-medium">{formatCurrency(b.total.amount, b.total.currency)}</p>
            </div>
            <ChevronRight className="h-4 w-4 text-muted-foreground" aria-hidden />
          </Link>
        </li>
      ))}
    </ul>
  );
};
