/**
 * Slice OPS.M.7 §4 — typed client for the onboarding wizard surface.
 *
 * Cross-tenant safety contract (§7): every Stripe call takes `tenantId` as
 * the first positional argument. Callers must pass the value they read from
 * `useMyTenant()` (which the backend derives from `ICurrentUser`). NEVER
 * read `tenantId` from URL state, localStorage, or env vars — the
 * TenantAuthorizationBehavior pipeline already rejects mismatches, but the
 * UI shouldn't even try.
 */
import { apiFetch } from './client';

export interface MeTenant {
  readonly id: string;
  readonly slug: string;
  readonly displayName: string;
  readonly status: 'PendingOnboarding' | 'Active' | 'Suspended' | 'Closed';
  readonly defaultCurrency: string;
  readonly platformFeeBps: number;
  readonly stripeAccountStatus: string | null;
  readonly chargesEnabled: boolean;
  readonly payoutsEnabled: boolean;
  readonly hasStripeAccount: boolean;
  readonly propertyCount: number;
  readonly onboarding: OnboardingProgress;
}

export interface OnboardingProgress {
  readonly isComplete: boolean;
  readonly nextStep:
    | 'Welcome'
    | 'CreateProperty'
    | 'ConnectStripe'
    | 'AwaitingVerification'
    | 'Done';
}

export interface StripeAccountLink {
  readonly url: string;
  readonly expiresAt: string;
}

export const getMyTenant = (): Promise<MeTenant> =>
  apiFetch<MeTenant>('/api/v1/me/tenant');

export const onboardTenantStripe = (
  tenantId: string,
  country = 'US',
): Promise<{ stripeAccountId: string }> =>
  apiFetch(
    `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/onboard`,
    { method: 'POST', body: { country } },
  );

export const generateStripeAccountLink = (
  tenantId: string,
): Promise<StripeAccountLink> =>
  apiFetch(
    `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/account-link`,
    { method: 'POST' },
  );

export const openStripeLoginLink = (
  tenantId: string,
): Promise<{ url: string }> =>
  apiFetch(
    `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/login-link`,
    { method: 'POST' },
  );
