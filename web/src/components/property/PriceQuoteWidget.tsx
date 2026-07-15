'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { computeQuote, type Quote } from '@/lib/api/pricing';
import { createHold, placeBooking } from '@/lib/api/booking';
import { getAvailability, type BlockedRange } from '@/lib/api/catalog';
import { ApiProblemError } from '@/lib/api/client';
import { useAuth } from '@/lib/auth/useAuth';
import { formatCurrency } from '@/lib/utils/currency';

const today = () => new Date().toISOString().slice(0, 10);
const addDays = (yyyymmdd: string, days: number): string => {
  const d = new Date(yyyymmdd + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

const isRangeBlocked = (checkin: string, checkout: string, blocked: readonly BlockedRange[]): BlockedRange | null => {
  for (const b of blocked) {
    if (b.start < checkout && checkin < b.end) return b;
  }
  return null;
};

interface PriceQuoteWidgetProps {
  readonly propertyId: string;
  readonly maxGuests: number;
}

export const PriceQuoteWidget = ({ propertyId, maxGuests }: PriceQuoteWidgetProps) => {
  const router = useRouter();
  const { isAuthenticated, signIn } = useAuth();
  const [checkin, setCheckin] = useState(() => addDays(today(), 14));
  const [checkout, setCheckout] = useState(() => addDays(today(), 17));
  const [guests, setGuests] = useState(2);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [agreed, setAgreed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [booking, setBooking] = useState(false);
  const [blocked, setBlocked] = useState<readonly BlockedRange[]>([]);

  const conflict = isRangeBlocked(checkin, checkout, blocked);
  const datesValid = checkout > checkin;

  const fetchQuote = async (cin: string, cout: string, g: number, signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const q = await computeQuote(propertyId, { checkin: cin, checkout: cout, guests: g }, signal);
      setQuote(q);
    } catch (err) {
      if (signal?.aborted) return;
      setQuote(null);
      if (err instanceof ApiProblemError) {
        setError(err.problem.detail ?? err.message);
      } else {
        setError(err instanceof Error ? err.message : 'Quote failed');
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  };

  const onBook = async () => {
    // F11.7.1: anonymous user clicking Book should be redirected to Entra
    // sign-in instead of letting the /bookings/holds call 401. After
    // sign-in MSAL returns the user to this same URL; the auto-quote
    // useEffect re-fires, and Book becomes clickable.
    if (!isAuthenticated) {
      signIn();
      return;
    }
    setBooking(true);
    setError(null);
    try {
      // Slice 0.1 + 0.2 closed the booking-race by requiring a Redis hold to
      // be consumed inside the serializable placeBooking transaction. Create
      // the hold first, then place the booking with its id.
      const hold = await createHold(propertyId, checkin, checkout, guests);
      const result = await placeBooking({
        holdId: hold.id,
        propertyId,
        checkinDate: checkin,
        checkoutDate: checkout,
        guestCount: guests,
        guests: [{ fullName: 'Primary guest', isPrimary: true }],
        agreedToHouseRules: true,
        applyLoyaltyDiscount: false,
      });
      router.push(`/bookings/${result.id}`);
    } catch (err) {
      if (err instanceof ApiProblemError) {
        setError(err.problem.detail ?? err.message);
      } else {
        setError(err instanceof Error ? err.message : 'Booking failed');
      }
    } finally {
      setBooking(false);
    }
  };

  // Initial mount: fetch the year-of-availability map so the date picker
  // can warn before Book. Quote auto-fetch is handled by the effect below.
  useEffect(() => {
    void (async () => {
      try {
        const from = today();
        const to = addDays(from, 365);
        const a = await getAvailability(propertyId, from, to);
        setBlocked(a.blocked);
      } catch {
        // Non-fatal: server-side overlap guard is the final safety net.
      }
    })();
  }, [propertyId]);

  // F11.7.1: auto-recompute the quote whenever check-in / check-out /
  // guests changes. Debounced 300ms so the user can finish typing /
  // tabbing without firing a quote per keystroke. AbortController
  // cancels in-flight quotes when the inputs change again before the
  // response lands (prevents a stale older quote replacing a newer one).
  // Pre-F11.7.1: required a manual Get-quote click; if the user clicked
  // Book without it, the booking went out with a stale quote (or none).
  useEffect(() => {
    if (!datesValid) {
      setQuote(null);
      setError(null);
      return;
    }
    const controller = new AbortController();
    const t = setTimeout(() => {
      void fetchQuote(checkin, checkout, guests, controller.signal);
    }, 300);
    return () => {
      clearTimeout(t);
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [propertyId, checkin, checkout, guests, datesValid]);

  return (
    <div className="space-y-4">
      <p className="text-sm font-medium">Pricing</p>

      <div className="grid grid-cols-2 gap-3">
        <label className="block text-xs">
          <span className="block text-muted-foreground">Check-in</span>
          <input
            type="date"
            value={checkin}
            min={today()}
            onChange={(e) => setCheckin(e.target.value)}
            className="mt-1 min-h-11 w-full rounded-md border border-border bg-background px-3 text-sm"
          />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Check-out</span>
          <input
            type="date"
            value={checkout}
            min={addDays(checkin, 1)}
            onChange={(e) => setCheckout(e.target.value)}
            className="mt-1 min-h-11 w-full rounded-md border border-border bg-background px-3 text-sm"
          />
        </label>
      </div>

      <label className="block text-xs">
        <span className="block text-muted-foreground">Guests</span>
        <input
          type="number"
          min={1}
          max={maxGuests}
          value={guests}
          onChange={(e) => setGuests(Math.max(1, Math.min(maxGuests, Number(e.target.value) || 1)))}
          className="mt-1 min-h-11 w-full rounded-md border border-border bg-background px-3 text-sm"
        />
      </label>

      {/* F11.7.1 — Get-quote button removed. Quote auto-fetches on
          checkin/checkout/guests change (debounced 300ms). The loading
          indicator surfaces inline below. */}
      {loading && (
        <div className="text-xs text-muted-foreground">Calculating…</div>
      )}

      {conflict && !error && (
        <div className="rounded-md border border-yellow-500/40 bg-yellow-50 p-3 text-xs text-yellow-900 dark:bg-yellow-900/20 dark:text-yellow-200">
          These dates overlap with an existing booking ({conflict.start} to {conflict.end}). Please pick different dates.
        </div>
      )}

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-xs text-destructive">
          {error}
        </div>
      )}

      {quote && !error && (
        <div className="space-y-3 border-t border-border pt-3 text-sm">
          <div className="space-y-1.5">
            <div className="flex justify-between text-muted-foreground">
              <span>
                {quote.nightly.length} nights × {formatCurrency(quote.subtotal.amount / Math.max(1, quote.nightly.length), quote.subtotal.currency)}
              </span>
              <span>{formatCurrency(quote.subtotal.amount, quote.subtotal.currency)}</span>
            </div>
            {quote.fees.map((f) => (
              <div key={f.label} className="flex justify-between text-muted-foreground">
                <span>{f.label}</span>
                <span>{formatCurrency(f.amount.amount, f.amount.currency)}</span>
              </div>
            ))}
            <div className="flex justify-between border-t border-border pt-2 font-medium">
              <span>Total</span>
              <span>{formatCurrency(quote.total.amount, quote.total.currency)}</span>
            </div>
          </div>

          <label className="flex min-h-11 items-center gap-2 py-2 text-xs">
            <input
              type="checkbox"
              checked={agreed}
              onChange={(e) => setAgreed(e.target.checked)}
              className="h-5 w-5 rounded border-border"
            />
            <span className="text-muted-foreground">I agree to the house rules.</span>
          </label>

          <button
            type="button"
            onClick={() => void onBook()}
            disabled={booking || (isAuthenticated && !agreed) || conflict !== null}
            className="min-h-11 w-full rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            {booking
              ? 'Booking…'
              : conflict
                ? 'Dates unavailable'
                : !isAuthenticated
                  ? 'Sign in to book'
                  : 'Book this stay'}
          </button>

          <p className="text-xs text-muted-foreground">
            {isAuthenticated
              ? 'Your card will be authorized but not charged until the host confirms.'
              : 'Sign in to continue. We’ll return you here after.'}
          </p>
        </div>
      )}
    </div>
  );
};
