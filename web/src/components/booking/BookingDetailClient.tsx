'use client';

import { ArrowRight, CheckCircle2, Clock, XCircle } from 'lucide-react';

import { ApiProblemError } from '@/lib/api/client';
import { getBooking, type Booking, type BookingStatus } from '@/lib/api/booking';
import { formatCurrency } from '@/lib/utils/currency';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import { BookingAutoRefresh } from './BookingAutoRefresh';
import { CancelBookingButton } from './CancelBookingButton';
import { ReviewSubmitForm } from './ReviewSubmitForm';
import { StripePaymentForm } from './StripePaymentForm';

interface Props {
  readonly id: string;
}

const StatusPill = ({ status }: { status: BookingStatus }) => {
  const map: Record<BookingStatus, { label: string; cls: string }> = {
    Draft: { label: 'Draft', cls: 'bg-muted text-muted-foreground' },
    Tentative: { label: 'Awaiting host', cls: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300' },
    Confirmed: { label: 'Confirmed', cls: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300' },
    CheckedIn: { label: 'Checked in', cls: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300' },
    CheckedOut: { label: 'Checked out', cls: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300' },
    Completed: { label: 'Completed', cls: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300' },
    Cancelled: { label: 'Cancelled', cls: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300' },
    Rejected: { label: 'Rejected by host', cls: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300' },
    Disputed: { label: 'Disputed', cls: 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-300' },
    Refunded: { label: 'Refunded', cls: 'bg-muted text-muted-foreground' },
  };
  const { label, cls } = map[status];
  return <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${cls}`}>{label}</span>;
};

const Timeline = ({ booking }: { booking: Booking }) => {
  const steps: { label: string; date: string | null; icon: typeof CheckCircle2 }[] = [
    { label: 'Booking placed', date: booking.createdAt, icon: CheckCircle2 },
    {
      label: booking.status === 'Rejected' ? 'Rejected by host' : 'Confirmed by host',
      date: booking.confirmedAt ?? (booking.status === 'Rejected' ? booking.cancelledAt : null),
      icon: booking.status === 'Rejected' ? XCircle : booking.confirmedAt ? CheckCircle2 : Clock,
    },
  ];
  if (booking.status === 'Cancelled' || booking.status === 'Rejected') {
    steps.push({
      label: booking.status === 'Cancelled' ? 'Cancelled' : 'Closed',
      date: booking.cancelledAt,
      icon: XCircle,
    });
  } else {
    steps.push({ label: 'Checked in', date: null, icon: Clock });
    steps.push({ label: 'Checked out', date: null, icon: Clock });
  }

  return (
    <ol className="space-y-3">
      {steps.map((s, i) => {
        const Icon = s.icon;
        const done = s.date !== null;
        return (
          <li key={i} className="flex items-center gap-3">
            <Icon className={`h-5 w-5 ${done ? 'text-green-600' : 'text-muted-foreground'}`} aria-hidden />
            <div className="flex-1">
              <p className={`text-sm ${done ? 'font-medium text-foreground' : 'text-muted-foreground'}`}>{s.label}</p>
              {s.date && (
                <p className="text-xs text-muted-foreground">{new Date(s.date).toLocaleString()}</p>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
};

const Skeleton = () => (
  <div className="space-y-4">
    <div className="h-7 w-1/3 animate-pulse rounded bg-muted" />
    <div className="h-4 w-1/4 animate-pulse rounded bg-muted" />
    <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
      <div className="md:col-span-2 h-64 animate-pulse rounded-xl bg-muted" />
      <div className="h-64 animate-pulse rounded-xl bg-muted" />
    </div>
  </div>
);

/**
 * Slice OPS.M.10.2 F11.7.4.2 — Booking detail, on useAuthedQuery.
 * Replaces the F11.7.3-era bare useQuery wiring. The wrapper now
 * handles MSAL-readiness gating, the 403/404 -> null collapse, and
 * the 401-no-retry policy in one place; this component is now just
 * the render tree.
 */
export const BookingDetailClient = ({ id }: Props) => {
  const { data: booking, isLoading, isError, error, refetch, needsSignIn } = useAuthedQuery<Booking>({
    queryKey: ['booking', id],
    queryFn: () => getBooking(id),
  });

  if (needsSignIn) {
    return (
      <SignInGate
        title="Sign in to view this booking"
        description="Your booking is saved. Sign in to view its status, cancel, or pay."
      />
    );
  }

  if (isLoading) {
    return <Skeleton />;
  }

  if (isError) {
    const status = error instanceof ApiProblemError ? error.status : undefined;
    return (
      <div className="mx-auto max-w-md py-12 text-center">
        <h1 className="text-xl font-semibold">We couldn&apos;t load this booking.</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          {status ? `API returned ${status}.` : 'Network error.'} Try again in a moment.
        </p>
        <button
          type="button"
          onClick={() => void refetch()}
          className="mt-4 inline-flex items-center rounded-md border border-border bg-background px-4 py-2 text-sm font-medium hover:bg-accent"
        >
          Retry
        </button>
      </div>
    );
  }

  if (!booking) {
    return (
      <div className="mx-auto max-w-md py-12 text-center">
        <h1 className="text-xl font-semibold">Booking not found</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          The booking may have been removed, or you may not have access to it.
        </p>
      </div>
    );
  }

  const b = booking;
  return (
    <div className="space-y-6">
      <BookingAutoRefresh status={b.status} />
      <header className="space-y-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">Booking {b.reference}</h1>
          <StatusPill status={b.status} />
        </div>
        <p className="text-sm text-muted-foreground">{b.propertyTitle}</p>
      </header>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <article className="space-y-6 lg:col-span-2">
          <section className="rounded-xl border border-border bg-card p-5 space-y-4">
            <div className="flex items-center gap-4 text-sm">
              <div>
                <p className="text-xs text-muted-foreground">Check-in</p>
                <p className="font-medium">{b.checkinDate}</p>
              </div>
              <ArrowRight className="h-4 w-4 text-muted-foreground" aria-hidden />
              <div>
                <p className="text-xs text-muted-foreground">Check-out</p>
                <p className="font-medium">{b.checkoutDate}</p>
              </div>
              <div className="ml-auto text-right">
                <p className="text-xs text-muted-foreground">Guests</p>
                <p className="font-medium">{b.guestCount}</p>
              </div>
            </div>
            {b.specialRequests && (
              <div className="rounded-md bg-muted p-3 text-sm">
                <p className="mb-1 text-xs font-medium text-muted-foreground">Special requests</p>
                {b.specialRequests}
              </div>
            )}
          </section>

          <section className="rounded-xl border border-border bg-card p-5">
            <h2 className="mb-3 text-sm font-medium">Charges</h2>
            <table className="w-full text-sm">
              <tbody>
                {b.lineItems.map((li, i) => (
                  <tr key={i} className="border-b border-border last:border-b-0">
                    <td className="py-2 text-muted-foreground">{li.label}</td>
                    <td className="py-2 text-right">{formatCurrency(li.total.amount, li.total.currency)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td className="pt-3 font-medium">Total</td>
                  <td className="pt-3 text-right font-medium">
                    {formatCurrency(b.totals.total.amount, b.totals.total.currency)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </section>
        </article>

        <aside className="space-y-4">
          {b.status === 'Tentative' && (
            <section className="rounded-xl border border-border bg-card p-5">
              <h2 className="mb-3 text-sm font-medium">Payment</h2>
              <StripePaymentForm bookingId={b.id} />
            </section>
          )}
          {(b.status === 'Tentative' || b.status === 'Confirmed') && (
            <section className="rounded-xl border border-border bg-card p-5">
              <h2 className="mb-3 text-sm font-medium">Cancel</h2>
              <CancelBookingButton booking={b} />
            </section>
          )}
          {(b.status === 'CheckedOut' || b.status === 'Completed') && (
            <section className="rounded-xl border border-border bg-card p-5">
              <h2 className="mb-3 text-sm font-medium">Leave a review</h2>
              <ReviewSubmitForm bookingId={b.id} />
            </section>
          )}
          <section className="rounded-xl border border-border bg-card p-5">
            <h2 className="mb-3 text-sm font-medium">Timeline</h2>
            <Timeline booking={b} />
          </section>
        </aside>
      </div>
    </div>
  );
};
