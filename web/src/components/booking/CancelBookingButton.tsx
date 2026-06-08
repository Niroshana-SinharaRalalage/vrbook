'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { cancelBooking, type Booking } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';
import { formatCurrency } from '@/lib/utils/currency';

interface Props {
  readonly booking: Booking;
}

export const CancelBookingButton = ({ booking }: Props) => {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Refund preview: backend applies Refund__ServiceFeePercent. We DON'T know the
  // percent in the browser bundle today (it lives in API env). For now we just say
  // "refund per platform policy" rather than fake a number. Owner can wire a
  // dedicated /refund-policy endpoint later if a number is wanted in the preview.
  const isCaptured = booking.status === 'Confirmed';
  const total = booking.totals.total;

  const onConfirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await cancelBooking(booking.id, reason || 'Cancelled by guest');
      // Navigate away from the now-Cancelled booking detail rather than relying on
      // router.refresh() (Next.js App Router caches the server fetch heavily, the
      // status pill keeps showing the old value). /account/bookings re-fetches
      // fresh and shows the cancellation in context with the user's other trips.
      router.push('/account/bookings');
    } catch (err) {
      setBusy(false);
      setError(
        err instanceof ApiProblemError
          ? err.problem.detail ?? err.message
          : err instanceof Error
            ? err.message
            : 'Cancel failed',
      );
    }
  };

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="w-full rounded-md border border-destructive/40 px-3 py-2 text-sm font-medium text-destructive hover:bg-destructive/5"
      >
        Cancel booking
      </button>
    );
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">
        {isCaptured ? (
          <>
            You will be refunded for{' '}
            <span className="font-medium text-foreground">
              {formatCurrency(total.amount, total.currency)}
            </span>{' '}
            minus any platform service fee, back to your original payment method.
          </>
        ) : (
          <>This booking hasn&apos;t been charged yet. Cancelling will release the auth-hold immediately.</>
        )}
      </p>

      <label className="block text-xs">
        <span className="block text-muted-foreground">Reason (optional)</span>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={2}
          maxLength={500}
          className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm"
        />
      </label>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-xs text-destructive">
          {error}
        </div>
      )}

      <div className="flex gap-2">
        <button
          type="button"
          onClick={() => setOpen(false)}
          disabled={busy}
          className="flex-1 rounded-md border border-border px-3 py-2 text-sm hover:bg-accent disabled:opacity-50"
        >
          Keep booking
        </button>
        <button
          type="button"
          onClick={() => void onConfirm()}
          disabled={busy}
          className="flex-1 rounded-md bg-destructive px-3 py-2 text-sm font-medium text-white hover:bg-destructive/90 disabled:opacity-50"
        >
          {busy ? 'Cancelling…' : 'Confirm cancel'}
        </button>
      </div>
    </div>
  );
};
