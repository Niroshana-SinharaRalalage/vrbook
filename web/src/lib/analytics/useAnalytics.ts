'use client';

import { track, type AnalyticsEvent, type AnalyticsProps } from './analytics';

/**
 * VRB-311 — ergonomic hook for funnel instrumentation. `track` is consent-gated
 * at the source, so callers never need to check consent themselves.
 */
export const useAnalytics = (): {
  track: (name: AnalyticsEvent, props?: AnalyticsProps) => void;
} => ({ track });
