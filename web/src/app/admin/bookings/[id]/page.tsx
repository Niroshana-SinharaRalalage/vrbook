'use client';

import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { ArrowLeft, CheckCircle2, XCircle, Clock, AlertCircle } from 'lucide-react';

import {
  adminGetBooking,
  backdateCheckedOutAt,
  checkInBooking,
  checkOutBooking,
  confirmBooking,
  rejectBooking,
  type Booking,
  type BookingStatus,
} from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';
import { formatCurrency } from '@/lib/utils/currency';

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

const AdminBookingDetailPage = () => {
  const router = useRouter();
  const { id } = useParams<{ id: string }>();
  const [booking, setBooking] = useState<Booking | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [acting, setActing] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');

  const reload = async () => {
    try {
      const b = await adminGetBooking(id);
      setBooking(b);
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
  };

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  if (loading) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Booking</h1>
        <p className="text-sm text-muted-foreground">Loading…</p>
      </div>
    );
  }

  if (error || !booking) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Booking</h1>
        <div className="rounded-md border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error ?? 'Not found'}
        </div>
      </div>
    );
  }

  const onConfirm = async () => {
    if (!window.confirm('Confirm this booking? The guest’s card will be charged now.')) return;
    setActing(true);
    setError(null);
    try {
      const updated = await confirmBooking(booking.id);
      setBooking(updated);
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Confirm failed',
      );
    } finally {
      setActing(false);
    }
  };

  const onReject = async () => {
    setActing(true);
    setError(null);
    try {
      const updated = await rejectBooking(booking.id, rejectReason.trim() || 'Owner declined.');
      setBooking(updated);
      setRejectOpen(false);
      setRejectReason('');
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Reject failed',
      );
    } finally {
      setActing(false);
    }
  };

  const runAction = async (
    label: string,
    fn: (id: string) => Promise<Booking | void>,
    refresh = false,
  ) => {
    setActing(true);
    setError(null);
    try {
      const result = await fn(booking.id);
      if (result && typeof result === 'object' && 'status' in result) {
        setBooking(result as Booking);
      } else if (refresh) {
        const fresh = await adminGetBooking(booking.id);
        setBooking(fresh);
      }
    } catch (err) {
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : `${label} failed`,
      );
    } finally {
      setActing(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="text-sm text-muted-foreground">
        <Link href="/admin/bookings" className="inline-flex items-center gap-1 hover:text-foreground">
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to bookings
        </Link>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">
          Booking {booking.reference}
        </h1>
        <span
          className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${STATUS_PILL[booking.status]}`}
        >
          {booking.status === 'Tentative' ? 'Awaiting your decision' : booking.status}
        </span>
      </div>

      {(booking.status === 'Confirmed' || booking.status === 'CheckedIn' || booking.status === 'CheckedOut') && (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-blue-300 bg-blue-50 p-4 dark:border-blue-700 dark:bg-blue-900/20">
          <div className="flex items-start gap-3">
            <Clock className="mt-0.5 h-5 w-5 text-blue-700 dark:text-blue-300" aria-hidden />
            <div className="text-sm">
              <p className="font-medium text-blue-900 dark:text-blue-100">Stay lifecycle</p>
              <p className="text-blue-800 dark:text-blue-200">
                Move the booking through the stay lifecycle. CheckOut sets the
                clock for the daily completion sweep (24h later it flips to
                Completed and the post-stay loop fires). The Backdate button is
                a dev-only shortcut so the completion sweep can run today.
              </p>
            </div>
          </div>
          <div className="flex gap-2">
            {booking.status === 'Confirmed' && (
              <button
                onClick={() => void runAction('Check-in', checkInBooking)}
                disabled={acting}
                className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                <CheckCircle2 className="h-4 w-4" aria-hidden /> Check in
              </button>
            )}
            {booking.status === 'CheckedIn' && (
              <button
                onClick={() => void runAction('Check-out', checkOutBooking)}
                disabled={acting}
                className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                <CheckCircle2 className="h-4 w-4" aria-hidden /> Check out
              </button>
            )}
            {booking.status === 'CheckedOut' && (
              <button
                onClick={async () => {
                  if (!window.confirm('Backdate CheckedOutAt by 25h so the completion sweep can run today?')) return;
                  await runAction('Backdate', (id) => backdateCheckedOutAt(id, 25), true);
                  window.alert('Done. Now trigger the completion sweep in Azure:\n\naz containerapp job start -n caj-vrbook-completion-staging -g rg-vrbook-staging');
                }}
                disabled={acting}
                className="inline-flex items-center gap-1.5 rounded-md border border-blue-500 px-4 py-2 text-sm font-medium text-blue-700 hover:bg-blue-50 disabled:opacity-50 dark:text-blue-300 dark:hover:bg-blue-950/30"
              >
                <Clock className="h-4 w-4" aria-hidden /> Backdate -25h (dev)
              </button>
            )}
          </div>
        </div>
      )}

      {booking.status === 'Tentative' && (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-yellow-300 bg-yellow-50 p-4 dark:border-yellow-700 dark:bg-yellow-900/20">
          <div className="flex items-start gap-3">
            <AlertCircle className="mt-0.5 h-5 w-5 text-yellow-700 dark:text-yellow-300" aria-hidden />
            <div className="text-sm">
              <p className="font-medium text-yellow-900 dark:text-yellow-100">
                Decision needed
              </p>
              <p className="text-yellow-800 dark:text-yellow-200">
                Confirming charges the guest&apos;s card and locks in the dates. Rejecting
                releases the authorization without any charge.
                {booking.tentativeUntil && (
                  <span className="ml-1">
                    Auto-resolves at {new Date(booking.tentativeUntil).toLocaleString()}.
                  </span>
                )}
              </p>
            </div>
          </div>
          <div className="flex gap-2">
            <button
              onClick={() => setRejectOpen(true)}
              disabled={acting}
              className="inline-flex items-center gap-1.5 rounded-md border border-red-500 px-4 py-2 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-50 dark:text-red-300 dark:hover:bg-red-950/30"
            >
              <XCircle className="h-4 w-4" aria-hidden /> Reject
            </button>
            <button
              onClick={() => void onConfirm()}
              disabled={acting}
              className="inline-flex items-center gap-1.5 rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
            >
              <CheckCircle2 className="h-4 w-4" aria-hidden /> Confirm
            </button>
          </div>
        </div>
      )}

      {rejectOpen && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/40 p-4">
          <div className="w-full max-w-md rounded-xl border border-border bg-background p-6 shadow-2xl">
            <h3 className="text-base font-medium">Reject booking?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              The guest will be told the host couldn&apos;t accept the reservation. Their card
              authorization is released; no charge is made.
            </p>
            <label className="mt-4 block text-sm">
              Reason <span className="text-xs text-muted-foreground">(optional, visible to guest)</span>
              <textarea
                rows={3}
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
                placeholder="e.g. The property is unavailable on these dates."
                className="mt-1 w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-1 focus:ring-brand-maroon-600"
              />
            </label>
            <div className="mt-4 flex justify-end gap-2">
              <button
                onClick={() => setRejectOpen(false)}
                disabled={acting}
                className="rounded-md border border-input bg-background px-4 py-2 text-sm hover:bg-accent"
              >
                Cancel
              </button>
              <button
                onClick={() => void onReject()}
                disabled={acting}
                className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
              >
                {acting ? 'Rejecting…' : 'Reject booking'}
              </button>
            </div>
          </div>
        </div>
      )}

      {error && (
        <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <section className="rounded-xl border border-border bg-card p-6">
            <h2 className="mb-4 text-base font-medium">Overview</h2>
            <dl className="grid gap-4 text-sm md:grid-cols-2">
              <Row label="Property" value={booking.propertyTitle} />
              <Row label="Guest" value={`${booking.guestDisplayName} (${booking.guestCount})`} />
              <Row label="Check-in" value={booking.checkinDate} />
              <Row label="Check-out" value={booking.checkoutDate} />
              <Row label="Created" value={new Date(booking.createdAt).toLocaleString()} />
              <Row label="Cancellation policy" value={booking.cancellationPolicy} />
              {booking.specialRequests && (
                <Row label="Special requests" value={booking.specialRequests} full />
              )}
            </dl>
          </section>

          <section className="rounded-xl border border-border bg-card p-6">
            <h2 className="mb-4 text-base font-medium">Charges</h2>
            <table className="w-full text-sm">
              <tbody>
                {booking.lineItems.map((li, i) => (
                  <tr key={i} className="border-b border-border last:border-b-0">
                    <td className="py-2 text-muted-foreground">{li.label}</td>
                    <td className="py-2 text-right">
                      {formatCurrency(li.total.amount, li.total.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td className="pt-3 font-medium">Total</td>
                  <td className="pt-3 text-right font-medium">
                    {formatCurrency(booking.totals.total.amount, booking.totals.total.currency)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </section>
        </div>

        <aside className="space-y-6">
          <section className="rounded-xl border border-border bg-card p-6">
            <h2 className="mb-3 text-sm font-medium">Timeline</h2>
            <ol className="space-y-3 text-sm">
              <TimelineItem
                label="Booking placed"
                date={booking.createdAt}
                done
                icon={CheckCircle2}
              />
              <TimelineItem
                label={
                  booking.status === 'Rejected' ? 'Rejected by host' : 'Confirmed by host'
                }
                date={
                  booking.confirmedAt ??
                  (booking.status === 'Rejected' ? booking.cancelledAt : null)
                }
                done={booking.confirmedAt !== null || booking.status === 'Rejected'}
                icon={
                  booking.status === 'Rejected'
                    ? XCircle
                    : booking.confirmedAt
                      ? CheckCircle2
                      : Clock
                }
              />
              {booking.cancelledAt && (
                <TimelineItem
                  label={booking.status === 'Cancelled' ? 'Cancelled' : 'Closed'}
                  date={booking.cancelledAt}
                  done
                  icon={XCircle}
                />
              )}
            </ol>
            {booking.cancellationReason && (
              <p className="mt-3 text-xs text-muted-foreground">
                Reason: {booking.cancellationReason}
              </p>
            )}
          </section>

          {booking.paymentIntentId && (
            <section className="rounded-xl border border-border bg-card p-6">
              <h2 className="mb-3 text-sm font-medium">Payment</h2>
              <p className="text-xs text-muted-foreground">
                Intent: <span className="font-mono">{booking.paymentIntentId}</span>
              </p>
            </section>
          )}
        </aside>
      </div>
    </div>
  );
};

const Row = ({
  label,
  value,
  full = false,
}: {
  label: string;
  value: string;
  full?: boolean;
}) => (
  <div className={full ? 'md:col-span-2' : ''}>
    <dt className="text-xs text-muted-foreground">{label}</dt>
    <dd className="mt-0.5">{value}</dd>
  </div>
);

const TimelineItem = ({
  label,
  date,
  done,
  icon: Icon,
}: {
  label: string;
  date: string | null;
  done: boolean;
  icon: typeof CheckCircle2;
}) => (
  <li className="flex items-start gap-3">
    <Icon
      className={`mt-0.5 h-4 w-4 ${done ? 'text-green-600' : 'text-muted-foreground'}`}
      aria-hidden
    />
    <div className="flex-1">
      <p className={`text-sm ${done ? 'font-medium' : 'text-muted-foreground'}`}>{label}</p>
      {date && (
        <p className="text-xs text-muted-foreground">{new Date(date).toLocaleString()}</p>
      )}
    </div>
  </li>
);

export default AdminBookingDetailPage;
