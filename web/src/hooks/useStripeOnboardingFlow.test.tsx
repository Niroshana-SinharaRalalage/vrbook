/**
 * Slice OPS.M.7 Step 6 — pins the two-call Stripe orchestration:
 * onboard (if needed) → account-link → redirect.
 */
import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useStripeOnboardingFlow } from './useStripeOnboardingFlow';
import * as tenantApi from '@/lib/api/tenant';

const tenantBase = { id: 'tnt-1', hasStripeAccount: false } as const;

beforeEach(() => {
  vi.spyOn(tenantApi, 'onboardTenantStripe').mockResolvedValue({
    stripeAccountId: 'acct_seed',
  });
  vi.spyOn(tenantApi, 'generateStripeAccountLink').mockResolvedValue({
    url: 'https://stripe.example/onboarding',
    expiresAt: new Date().toISOString(),
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('useStripeOnboardingFlow', () => {
  it('calls onboard then account-link then redirects when no Stripe account', async () => {
    const redirector = vi.fn();
    const { result } = renderHook(() =>
      useStripeOnboardingFlow(tenantBase, redirector),
    );

    await act(() => result.current.start());

    expect(tenantApi.onboardTenantStripe).toHaveBeenCalledWith('tnt-1');
    expect(tenantApi.generateStripeAccountLink).toHaveBeenCalledWith('tnt-1');
    expect(redirector).toHaveBeenCalledWith('https://stripe.example/onboarding');
  });

  it('skips onboard call when tenant already has a Stripe account', async () => {
    const redirector = vi.fn();
    const { result } = renderHook(() =>
      useStripeOnboardingFlow({ ...tenantBase, hasStripeAccount: true }, redirector),
    );

    await act(() => result.current.start());

    expect(tenantApi.onboardTenantStripe).not.toHaveBeenCalled();
    expect(tenantApi.generateStripeAccountLink).toHaveBeenCalledWith('tnt-1');
    expect(redirector).toHaveBeenCalledWith('https://stripe.example/onboarding');
  });

  it('sets error state and does not redirect when account-link fails', async () => {
    vi.spyOn(tenantApi, 'generateStripeAccountLink').mockRejectedValue(
      new Error('boom'),
    );
    const redirector = vi.fn();
    const { result } = renderHook(() =>
      useStripeOnboardingFlow(tenantBase, redirector),
    );

    await act(() => result.current.start());

    await waitFor(() => expect(result.current.status).toBe('error'));
    expect(result.current.error).toBe('boom');
    expect(redirector).not.toHaveBeenCalled();
  });

  it('sets error state and does not call account-link when onboard fails', async () => {
    vi.spyOn(tenantApi, 'onboardTenantStripe').mockRejectedValue(
      new Error('onboard-fail'),
    );
    const redirector = vi.fn();
    const { result } = renderHook(() =>
      useStripeOnboardingFlow(tenantBase, redirector),
    );

    await act(() => result.current.start());

    await waitFor(() => expect(result.current.status).toBe('error'));
    expect(tenantApi.generateStripeAccountLink).not.toHaveBeenCalled();
    expect(redirector).not.toHaveBeenCalled();
  });

  it('exposes loading status while in flight', async () => {
    let resolveLink: (v: { url: string; expiresAt: string }) => void = () => {};
    vi.spyOn(tenantApi, 'generateStripeAccountLink').mockReturnValue(
      new Promise((r) => {
        resolveLink = r;
      }),
    );
    const { result } = renderHook(() =>
      useStripeOnboardingFlow(tenantBase, vi.fn()),
    );

    let startPromise: Promise<void> = Promise.resolve();
    act(() => {
      startPromise = result.current.start();
    });
    await waitFor(() => expect(result.current.status).toBe('loading'));
    resolveLink({ url: 'https://stripe.example', expiresAt: '' });
    await act(async () => {
      await startPromise;
    });
  });
});
