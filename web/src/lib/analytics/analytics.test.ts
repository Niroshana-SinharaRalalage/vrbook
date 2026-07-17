import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Mock the App Insights SDK so we can assert whether it's loaded + used.
const trackEvent = vi.fn();
const loadAppInsights = vi.fn();
const setEnabled = vi.fn();
const AICtor = vi.fn().mockImplementation(() => ({
  loadAppInsights,
  trackEvent,
  getCookieMgr: () => ({ setEnabled }),
}));
vi.mock('@microsoft/applicationinsights-web', () => ({ ApplicationInsights: AICtor }));

describe('analytics — consent gating (VRB-311)', () => {
  beforeEach(() => {
    vi.resetModules();
    trackEvent.mockClear();
    loadAppInsights.mockClear();
    AICtor.mockClear();
  });
  afterEach(() => vi.unstubAllEnvs());

  it('is a hard no-op when no connection string is configured (safe-disabled)', async () => {
    vi.stubEnv('NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING', '');
    const mod = await import('./analytics');
    mod.setAnalyticsConsent(true);
    mod.track('page_view');
    expect(mod.isAnalyticsConfigured()).toBe(false);
    expect(loadAppInsights).not.toHaveBeenCalled();
    expect(trackEvent).not.toHaveBeenCalled();
  });

  it('does NOT import/load the SDK or emit before consent (the compliance invariant)', async () => {
    vi.stubEnv('NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING', 'InstrumentationKey=abc');
    const mod = await import('./analytics');
    // No consent yet — several events fire.
    mod.track('search_performed');
    mod.track('quote_viewed');
    // Give any (wrongly-scheduled) microtask a chance.
    await Promise.resolve();
    expect(AICtor).not.toHaveBeenCalled();
    expect(loadAppInsights).not.toHaveBeenCalled();
    expect(trackEvent).not.toHaveBeenCalled();
  });

  it('loads the SDK once and flushes buffered events after consent', async () => {
    vi.stubEnv('NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING', 'InstrumentationKey=abc');
    const mod = await import('./analytics');
    mod.setAnalyticsConsent(true); // triggers async lazy init
    mod.track('booking_confirmed'); // buffered until init resolves
    await vi.waitFor(() => expect(loadAppInsights).toHaveBeenCalledTimes(1));
    await vi.waitFor(() =>
      expect(trackEvent).toHaveBeenCalledWith({ name: 'booking_confirmed' }, undefined),
    );
  });

  it('stops emitting after opt-out', async () => {
    vi.stubEnv('NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING', 'InstrumentationKey=abc');
    const mod = await import('./analytics');
    mod.setAnalyticsConsent(true);
    await vi.waitFor(() => expect(loadAppInsights).toHaveBeenCalled());
    mod.setAnalyticsConsent(false); // opt-out
    trackEvent.mockClear();
    mod.track('page_view');
    expect(trackEvent).not.toHaveBeenCalled();
    expect(setEnabled).toHaveBeenCalledWith(false);
  });
});
