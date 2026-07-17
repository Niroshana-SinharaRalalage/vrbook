/**
 * VRB-311 — consent-gated analytics on Application Insights.
 *
 * Compliance invariant (the story's #1 rollback trigger): **no telemetry leaves
 * the browser before the visitor accepts analytics.** Guaranteed structurally —
 * the App Insights SDK is NEVER imported at module top; the dynamic
 * `import()` fires only from {@link setAnalyticsConsent}(true). Before consent,
 * `track()` is a hard no-op (events are dropped, not queued). Only AFTER consent
 * — during the brief async SDK load — are events buffered in memory and flushed
 * on init, so nothing is lost yet nothing is sent early.
 *
 * Safe-disabled: when `NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING` is
 * absent (DEVOPS hasn't landed it yet), everything no-ops — build + runtime are
 * unaffected; analytics simply "turns on" once the per-env string is injected.
 */
import type { ApplicationInsights } from '@microsoft/applicationinsights-web';

export type AnalyticsEvent =
  | 'page_view'
  | 'search_performed'
  | 'quote_viewed'
  | 'booking_started'
  | 'booking_tentative'
  | 'booking_confirmed';

export type AnalyticsProps = Record<string, string | number | boolean | undefined>;

const CONNECTION_STRING = process.env.NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING;
const MAX_BUFFER = 50;

let consented = false;
let client: ApplicationInsights | null = null;
let initializing = false;
const buffer: { name: AnalyticsEvent; props?: AnalyticsProps }[] = [];

export const isAnalyticsConfigured = (): boolean => Boolean(CONNECTION_STRING);

/** Record a funnel/page event. No-op until analytics consent is given AND configured. */
export const track = (name: AnalyticsEvent, props?: AnalyticsProps): void => {
  if (!consented || !CONNECTION_STRING) return; // never emit before consent
  if (client) {
    client.trackEvent({ name }, props);
    return;
  }
  // Consented but the SDK is still loading — buffer briefly, flush on init.
  if (buffer.length < MAX_BUFFER) buffer.push({ name, props });
};

const flush = (): void => {
  if (!client) return;
  for (const e of buffer) client.trackEvent({ name: e.name }, e.props);
  buffer.length = 0;
};

const init = async (): Promise<void> => {
  if (client || initializing || !CONNECTION_STRING) return;
  initializing = true;
  try {
    const { ApplicationInsights: AI } = await import('@microsoft/applicationinsights-web');
    const ai = new AI({
      config: {
        connectionString: CONNECTION_STRING,
        // We drive page views ourselves (post-consent) — no SPA auto-tracking.
        enableAutoRouteTracking: false,
        disableFetchTracking: true,
        disableAjaxTracking: true,
      },
    });
    ai.loadAppInsights();
    client = ai;
    flush();
  } finally {
    initializing = false;
  }
};

/**
 * Called by the consent layer whenever analytics consent changes. `true` lazily
 * boots the SDK + flushes; `false` opts out — disables AI cookies, drops the
 * buffer, and forgets the instance so nothing more is sent.
 */
export const setAnalyticsConsent = (enabled: boolean): void => {
  consented = enabled;
  if (enabled) {
    void init();
    return;
  }
  if (client) {
    try {
      client.getCookieMgr().setEnabled(false);
    } catch {
      /* best-effort teardown */
    }
  }
  client = null;
  buffer.length = 0;
};

/** Test-only: reset module state between cases. */
export const __resetAnalyticsForTest = (): void => {
  consented = false;
  client = null;
  initializing = false;
  buffer.length = 0;
};
