'use client';

import { useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Check, Star } from 'lucide-react';
import { submitReview, type Review } from '@/lib/api/reviews';
import { getBooking, type Booking } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';

const StarPicker = ({ value, onChange, disabled }: { value: number; onChange: (v: number) => void; disabled?: boolean }) => (
  <div className="flex gap-1" aria-label="Rating">
    {[1, 2, 3, 4, 5].map((n) => (
      <button
        key={n}
        type="button"
        onClick={() => onChange(n)}
        disabled={disabled}
        className="rounded p-1 transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
        aria-label={`${n} star${n === 1 ? '' : 's'}`}
      >
        <Star
          className={`h-7 w-7 ${n <= value ? 'fill-yellow-400 text-yellow-400' : 'text-muted-foreground'}`}
        />
      </button>
    ))}
  </div>
);

const extractErr = (err: unknown, fallback: string): string => {
  if (err instanceof ApiProblemError) return err.problem.detail ?? err.message;
  if (err instanceof Error) return err.message;
  return fallback;
};

// Slice OPS.M.10.2 F11.7.4.5 — booking fetch migrated onto
// useAuthedQuery (gated). submitReview stays a manual call since
// it's a mutation invoked from the submit handler, not a query.
const ReviewBookingPage = () => {
  const params = useParams<{ id: string }>();
  const bookingId = params?.id ?? '';

  const { data: booking, isLoading, isError, error: queryError, needsSignIn } = useAuthedQuery<Booking>({
    queryKey: ['booking', bookingId],
    queryFn: () => getBooking(bookingId),
    enabled: !!bookingId,
  });

  const [rating, setRating] = useState(0);
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitted, setSubmitted] = useState<Review | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  if (needsSignIn) {
    return <SignInGate title="Sign in to review your stay" />;
  }

  if (isLoading) {
    return <p className="p-6 text-sm text-muted-foreground">Loading booking…</p>;
  }
  if (!booking) {
    return (
      <div className="space-y-4 p-6">
        <p className="text-sm text-destructive">
          {isError ? extractErr(queryError, 'Could not load the booking.') : 'Booking not found.'}
        </p>
        <Link href="/account/bookings" className="text-sm underline">
          Back to my trips
        </Link>
      </div>
    );
  }

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (rating < 1) {
      setSubmitError('Pick a 1-5 star rating.');
      return;
    }
    setBusy(true);
    setSubmitError(null);
    try {
      const result = await submitReview(bookingId, { rating, body: body.trim() });
      setSubmitted(result);
    } catch (err) {
      setSubmitError(extractErr(err, 'Failed to submit review.'));
    } finally {
      setBusy(false);
    }
  };

  if (submitted) {
    return (
      <div className="mx-auto max-w-xl space-y-4 p-6">
        <div className="flex items-center gap-2 rounded-md border border-emerald-300 bg-emerald-50 p-4 text-emerald-900 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-200">
          <Check className="h-5 w-5" />
          <span className="text-sm">Thanks — your {submitted.rating}-star review is live.</span>
        </div>
        <div className="rounded-md border border-border bg-card p-4 text-sm">
          <div className="font-medium">{submitted.guestDisplayName}</div>
          <div className="mt-1 text-muted-foreground">Rating: {submitted.rating} / 5</div>
          {submitted.body && <p className="mt-2 whitespace-pre-wrap">{submitted.body}</p>}
        </div>
        <Link
          href={`/properties/${booking.propertyId}`}
          className="inline-block text-sm underline"
        >
          See your review on the property page
        </Link>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-xl space-y-6 p-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">How was your stay?</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {booking.propertyTitle} · {booking.checkinDate} → {booking.checkoutDate}
        </p>
      </header>

      <form onSubmit={onSubmit} className="space-y-5">
        <div className="space-y-2">
          <label className="text-sm font-medium">Rating</label>
          <StarPicker value={rating} onChange={setRating} disabled={busy} />
        </div>

        <div className="space-y-2">
          <label className="text-sm font-medium" htmlFor="body">
            What stood out? <span className="font-normal text-muted-foreground">(optional)</span>
          </label>
          <textarea
            id="body"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={6}
            maxLength={4000}
            disabled={busy}
            className="w-full rounded-md border border-border bg-background p-3 text-sm"
            placeholder="A sentence or two helps future guests."
          />
          <div className="text-right text-xs text-muted-foreground">{body.length} / 4000</div>
        </div>

        {submitError && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
            {submitError}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <Link
            href="/account/bookings"
            className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent"
          >
            Skip for now
          </Link>
          <button
            type="submit"
            disabled={busy || rating < 1}
            className="rounded-md bg-brand-maroon-700 px-4 py-1.5 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            {busy ? 'Submitting…' : 'Submit review'}
          </button>
        </div>
      </form>
    </div>
  );
};

export default ReviewBookingPage;
