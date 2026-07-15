'use client';

import { useEffect, useMemo, useState } from 'react';
import { loadStripe, type Stripe } from '@stripe/stripe-js';
import { Elements, PaymentElement, useStripe, useElements } from '@stripe/react-stripe-js';
import { CheckCircle2 } from 'lucide-react';
import { getPaymentIntentForBooking, type PaymentIntent } from '@/lib/api/booking';
import { ApiProblemError } from '@/lib/api/client';

interface StripePaymentFormProps {
  readonly bookingId: string;
  /// Where Stripe should redirect after off-session 3DS flows. Defaults to the same page.
  readonly returnUrl?: string;
}

/**
 * Stripe PaymentIntent statuses that mean the guest still needs to enter
 * card details (the form should mount Elements). Any other status means the
 * card has already been authorised — see the AlreadyAuthorisedCard branch.
 */
const PAYABLE_STATUSES: ReadonlySet<string> = new Set([
  'requires_payment_method',
  'requires_confirmation',
  'requires_action',
]);

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
    try {
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
    } catch (e2) {
      // `confirmPayment()` can THROW (e.g. IntegrationError when the Payment
      // Element never mounted because the PI returned 400). Catching here
      // ensures the button always resets - previously the throw bypassed
      // the result.error branch and stuck the button at "Authorising...".
      setError(e2 instanceof Error ? e2.message : 'Payment failed.');
      setSubmitting(false);
    }
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
        className="min-h-11 w-full rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
      >
        {submitting ? 'Authorising…' : 'Authorise card'}
      </button>
      <p className="text-xs text-muted-foreground">
        You are placing an auth-hold, not a charge. The host captures funds only after confirming your booking.
      </p>
    </form>
  );
};

const AlreadyAuthorisedCard = ({ pi }: { pi: PaymentIntent }) => (
  <div className="space-y-2 rounded-md border border-emerald-200 bg-emerald-50 p-4 text-sm dark:border-emerald-900 dark:bg-emerald-950/40">
    <div className="flex items-center gap-2 font-medium text-emerald-900 dark:text-emerald-100">
      <CheckCircle2 className="h-4 w-4" />
      Card authorised
    </div>
    <p className="text-xs text-emerald-800 dark:text-emerald-200">
      Your card is held for the booking total. The host will capture funds when they confirm.
      If they don&apos;t confirm, the hold is released — no charge.
    </p>
    <p className="text-[11px] text-emerald-700/80 dark:text-emerald-300/80">
      Status: <code>{pi.status}</code>
    </p>
  </div>
);

export const StripePaymentForm = ({ bookingId }: StripePaymentFormProps) => {
  const [pi, setPi] = useState<PaymentIntent | null>(null);
  const [clientSecret, setClientSecret] = useState<string | null>(null);
  const [stripePromise, setStripePromise] = useState<Promise<Stripe | null> | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const dto = await getPaymentIntentForBooking(bookingId);
        setPi(dto.paymentIntent);
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
  if (!pi) {
    return <div className="h-32 animate-pulse rounded-md bg-muted" aria-label="Loading payment form" />;
  }
  // If the PI is past the payable state (already authorised + waiting for
  // owner capture, or fully captured, or canceled), don't try to mount
  // Stripe Elements - the API will return 400 and throw IntegrationError.
  // Show the status card instead.
  if (!PAYABLE_STATUSES.has(pi.status)) {
    return <AlreadyAuthorisedCard pi={pi} />;
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
