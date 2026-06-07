'use client';

import { useEffect, useMemo, useState } from 'react';
import { loadStripe, type Stripe } from '@stripe/stripe-js';
import { Elements, PaymentElement, useStripe, useElements } from '@stripe/react-stripe-js';
import { getPaymentIntentForBooking } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';

interface StripePaymentFormProps {
  readonly bookingId: string;
  /// Where Stripe should redirect after off-session 3DS flows. Defaults to the same page.
  readonly returnUrl?: string;
}

const PayButton = () => {
  const stripe = useStripe();
  const elements = useElements();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!stripe || !elements) return;
    setSubmitting(true);
    setError(null);
    const result = await stripe.confirmPayment({
      elements,
      confirmParams: { return_url: typeof window === 'undefined' ? '/' : window.location.href },
      redirect: 'if_required',
    });
    if (result.error) {
      setError(result.error.message ?? 'Payment failed.');
      setSubmitting(false);
      return;
    }
    // PI is now requires_capture. Owner will capture on Confirm.
    // Refresh the page so the booking detail re-renders with the updated PI status.
    window.location.reload();
  };

  return (
    <form onSubmit={onSubmit} className="space-y-3">
      <PaymentElement options={{ layout: 'tabs' }} />
      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-xs text-destructive">
          {error}
        </div>
      )}
      <button
        type="submit"
        disabled={!stripe || !elements || submitting}
        className="w-full rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
      >
        {submitting ? 'Authorising…' : 'Authorise card'}
      </button>
      <p className="text-xs text-muted-foreground">
        You are placing an auth-hold, not a charge. The host captures funds only after confirming your booking.
      </p>
    </form>
  );
};

export const StripePaymentForm = ({ bookingId }: StripePaymentFormProps) => {
  const [clientSecret, setClientSecret] = useState<string | null>(null);
  const [stripePromise, setStripePromise] = useState<Promise<Stripe | null> | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const dto = await getPaymentIntentForBooking(bookingId);
        setClientSecret(dto.clientSecret);
        setStripePromise(loadStripe(dto.publishableKey));
      } catch (err) {
        if (err instanceof ApiProblemError && err.status === 404) {
          setLoadError('Payment is not configured for this environment.');
        } else {
          setLoadError(err instanceof Error ? err.message : 'Failed to load payment.');
        }
      }
    })();
  }, [bookingId]);

  const options = useMemo(() => (clientSecret ? { clientSecret } : null), [clientSecret]);

  if (loadError) {
    return <div className="rounded-md bg-muted p-4 text-xs text-muted-foreground">{loadError}</div>;
  }
  if (!stripePromise || !options) {
    return <div className="h-32 animate-pulse rounded-md bg-muted" aria-label="Loading payment form" />;
  }
  return (
    <Elements stripe={stripePromise} options={options}>
      <PayButton />
    </Elements>
  );
};
