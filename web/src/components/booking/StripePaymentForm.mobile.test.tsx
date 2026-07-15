import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

// VRB-106 mobile-checkout guardrail: pin the ≥44px "Authorise card" target so
// the payment step stays tappable at 360px. Layout only — Stripe logic is
// stubbed, not exercised.

vi.mock('@stripe/stripe-js', () => ({ loadStripe: vi.fn(() => Promise.resolve({})) }));
vi.mock('@stripe/react-stripe-js', () => ({
  Elements: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  PaymentElement: () => <div data-testid="payment-element" />,
  useStripe: () => ({}),
  useElements: () => ({}),
}));
vi.mock('@/lib/api/booking', () => ({ getPaymentIntentForBooking: vi.fn() }));

import { getPaymentIntentForBooking } from '@/lib/api/booking';
import { StripePaymentForm } from './StripePaymentForm';

afterEach(() => vi.clearAllMocks());

describe('StripePaymentForm — mobile (360px) tap target', () => {
  it('gives the Authorise-card button a full-width >=44px target', async () => {
    vi.mocked(getPaymentIntentForBooking).mockResolvedValue({
      paymentIntent: { status: 'requires_payment_method' },
      clientSecret: 'cs_test',
      publishableKey: 'pk_test',
    } as never);

    render(<StripePaymentForm bookingId="b1" />);

    const btn = await screen.findByRole(
      'button',
      { name: /authorise card/i },
      { timeout: 3000 },
    );
    expect(btn.className).toContain('min-h-11');
    expect(btn.className).toContain('w-full');
  });
});
