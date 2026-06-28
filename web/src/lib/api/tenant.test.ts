/**
 * Slice OPS.M.7 Step 5 — pins the request shape of every onboarding-wizard
 * API call. Same intent as the server-side `MeTenantDtoShapeTests`: catch
 * a misnamed endpoint or missing tenant-id segment at PR time.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getMyTenant,
  onboardTenantStripe,
  generateStripeAccountLink,
  openStripeLoginLink,
} from './tenant';

const fetchMock = vi.fn();
const originalFetch = globalThis.fetch;

beforeEach(() => {
  // The client reads NEXT_PUBLIC_API_BASE_URL via process.env at request
  // time; in jsdom we feed it a known base so URL construction stays valid.
  (process.env as Record<string, string>).NEXT_PUBLIC_API_BASE_URL =
    'http://localhost:5000';
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(
    new Response(JSON.stringify({}), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  globalThis.fetch = fetchMock as unknown as typeof fetch;
});

afterEach(() => {
  globalThis.fetch = originalFetch;
});

const lastUrl = () => new URL(fetchMock.mock.calls[0]![0] as string);
const lastInit = () => fetchMock.mock.calls[0]![1] as RequestInit;

describe('tenant API client', () => {
  it('getMyTenant calls GET /api/v1/me/tenant', async () => {
    await getMyTenant();
    expect(lastUrl().pathname).toBe('/api/v1/me/tenant');
    expect(lastInit().method).toBe('GET');
  });

  it('onboardTenantStripe posts to /admin/tenants/{tenantId}/stripe/onboard', async () => {
    await onboardTenantStripe('abc-123');
    expect(lastUrl().pathname).toBe('/api/v1/admin/tenants/abc-123/stripe/onboard');
    expect(lastInit().method).toBe('POST');
    expect(lastInit().body).toContain('US');
  });

  it('onboardTenantStripe URL-encodes the tenant id', async () => {
    await onboardTenantStripe('a/b');
    expect(lastUrl().pathname).toBe('/api/v1/admin/tenants/a%2Fb/stripe/onboard');
  });

  it('generateStripeAccountLink posts to /stripe/account-link', async () => {
    await generateStripeAccountLink('abc-123');
    expect(lastUrl().pathname).toBe('/api/v1/admin/tenants/abc-123/stripe/account-link');
    expect(lastInit().method).toBe('POST');
  });

  it('openStripeLoginLink posts to /stripe/login-link', async () => {
    await openStripeLoginLink('abc-123');
    expect(lastUrl().pathname).toBe('/api/v1/admin/tenants/abc-123/stripe/login-link');
    expect(lastInit().method).toBe('POST');
  });

  it('every Stripe call requires tenantId as a positional argument (no global default)', () => {
    // Structural assertion - functions take tenantId in the first positional
    // slot. Cross-tenant safety: a future "default tenant" parameter
    // would let URL state leak into the call site. Sentinel test.
    expect(onboardTenantStripe.length).toBeGreaterThanOrEqual(1);
    expect(generateStripeAccountLink.length).toBe(1);
    expect(openStripeLoginLink.length).toBe(1);
  });
});
