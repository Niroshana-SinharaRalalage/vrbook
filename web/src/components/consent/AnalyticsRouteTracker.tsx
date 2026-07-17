'use client';

import { useEffect } from 'react';
import { usePathname, useSearchParams } from 'next/navigation';

import { track } from '@/lib/analytics/analytics';

/**
 * VRB-311 — client-side page-view tracker. Fires a consent-gated `page_view` on
 * every App-Router navigation. `track` is a no-op until analytics consent is
 * given, so this never emits early. Wrap in `<Suspense>` (useSearchParams).
 */
export const AnalyticsRouteTracker = () => {
  const pathname = usePathname();
  const searchParams = useSearchParams();

  useEffect(() => {
    const query = searchParams.toString();
    track('page_view', { path: query ? `${pathname}?${query}` : pathname });
  }, [pathname, searchParams]);

  return null;
};
