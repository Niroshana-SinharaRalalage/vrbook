/**
 * Slice OPS.2.2 — Stripe test-mode card numbers used by the guest/owner
 * checkout scenarios (OPS.2.4/2.5). Staging runs Stripe in test mode, so these
 * are the canonical Stripe-published test PANs — safe to keep in source
 * (they are NOT secrets). See docs/OPS_2_PLAYWRIGHT_PLAN.md §6.
 */
export const STRIPE_TEST_CARDS = {
  /** Frictionless success. */
  success: {
    number: '4242 4242 4242 4242',
    exp: '12/34',
    cvc: '123',
    zip: '42424',
  },
  /** Triggers a 3DS authentication challenge — the auth-required edge case. */
  authRequired: {
    number: '4000 0000 0000 9995',
    exp: '12/34',
    cvc: '123',
    zip: '42424',
  },
} as const;

export type StripeTestCard = (typeof STRIPE_TEST_CARDS)[keyof typeof STRIPE_TEST_CARDS];
