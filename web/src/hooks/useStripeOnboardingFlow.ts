/**
 * Slice OPS.M.7 §3.3 (D3) — orchestrates the two-call Stripe onboarding
 * flow: `onboard` (if no account) → `account-link` → window redirect to
 * the Stripe-hosted form.
 *
 * The `onboard` call is idempotent on tenant id (OPS.M.5 §3.13), so
 * re-clicking the "Connect Stripe" button after closing the Stripe tab
 * just regenerates the link and bounces back to Stripe — no duplicate
 * account creation.
 */
import { useCallback, useState } from 'react';
import {
  generateStripeAccountLink,
  onboardTenantStripe,
  type MeTenant,
} from '@/lib/api/tenant';

export type StripeFlowStatus = 'idle' | 'loading' | 'error';

export interface UseStripeOnboardingFlowResult {
  readonly status: StripeFlowStatus;
  readonly error: string | null;
  readonly start: () => Promise<void>;
}

export const useStripeOnboardingFlow = (
  tenant: Pick<MeTenant, 'id' | 'hasStripeAccount'>,
  redirector: (url: string) => void = (url) => {
    window.location.href = url;
  },
): UseStripeOnboardingFlowResult => {
  const [status, setStatus] = useState<StripeFlowStatus>('idle');
  const [error, setError] = useState<string | null>(null);

  const start = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      if (!tenant.hasStripeAccount) {
        await onboardTenantStripe(tenant.id);
      }
      const { url } = await generateStripeAccountLink(tenant.id);
      redirector(url);
    } catch (e) {
      setStatus('error');
      setError(e instanceof Error ? e.message : 'Stripe call failed');
    }
  }, [tenant.id, tenant.hasStripeAccount, redirector]);

  return { status, error, start };
};
