'use client';

import { useEffect, useRef } from 'react';

import { track, type AnalyticsEvent, type AnalyticsProps } from '@/lib/analytics/analytics';

/**
 * VRB-311 — fire a single consent-gated analytics event when this mounts. Lets a
 * server component (which can't call `track`) record a funnel event by rendering
 * this client child. No-op until analytics consent is given.
 */
export const TrackOnMount = ({
  event,
  props,
}: {
  readonly event: AnalyticsEvent;
  readonly props?: AnalyticsProps;
}) => {
  const fired = useRef(false);
  useEffect(() => {
    if (fired.current) return;
    fired.current = true;
    track(event, props);
  }, [event, props]);
  return null;
};
