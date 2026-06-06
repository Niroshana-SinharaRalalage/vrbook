'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { computeQuote, type Quote } from '@/lib/api/pricing';
import { placeBooking } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';
import { formatCurrency } from '@/lib/utils/currency';

const today = () => new Date().toISOString().slice(0, 10);
const addDays = (yyyymmdd: string, days: number): string => {
  const d = new Date(yyyymmdd + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

interface PriceQuoteWidgetProps {
  readonly propertyId: string;
  readonly maxGuests: number;
}

export const PriceQuoteWidget = ({ propertyId, maxGuests }: PriceQuoteWidgetProps) => {
  const router = useRouter();
  const [checkin, setCheckin] = useState(() => addDays(today(), 14));
  const [checkout, setCheckout] = useState(() => addDays(today(), 17));
  const [guests, setGuests] = useState(2);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [agreed, setAgreed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [booking, setBooking] = useState(false);

  const fetchQuote = async () => {
    setLoading(true);
    setError(null);
    try {
      const q = await computeQuote(propertyId, { checkin, checkout, guests });
      setQuote(q);
    } catch (err) {
      setQuote(null);
      if (err instanceof ApiProblemError) {
        setError(err.problem.detail ?? err.message);
      } else {
        setError(err instanceof Error ? err.message : 'Quote failed');
      }
    } finally {
      setLoading(false);
    }
  };

  const onBook = async () => {
    setBooking(true);
    setError(null);
    try {
      const result = await placeBooking({
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

  useEffect(() => {
    void fetchQuote();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [propertyId]);

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
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
          />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Check-out</span>
          <input
            type="date"
            value={checkout}
            min={addDays(checkin, 1)}
            onChange={(e) => setCheckout(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
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
          className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
        />
      </label>

      <button
        type="button"
        onClick={() => void fetchQuote()}
        disabled={loading || booking}
        className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm hover:bg-accent disabled:opacity-50"
      >
        {loading ? 'Calculating…' : 'Get quote'}
      </button>

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

          <label className="flex items-start gap-2 text-xs">
            <input
              type="checkbox"
              checked={agreed}
              onChange={(e) => setAgreed(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-border"
            />
            <span className="text-muted-foreground">I agree to the house rules.</span>
          </label>

          <button
            type="button"
            onClick={() => void onBook()}
            disabled={booking || !agreed}
            className="w-full rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            {booking ? 'Booking…' : 'Book this stay'}
          </button>

          <p className="text-xs text-muted-foreground">
            You won&apos;t be charged yet. Payment integration lands in Agent A5.
          </p>
        </div>
      )}
    </div>
  );
};
