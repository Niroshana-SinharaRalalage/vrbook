'use client';

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';

import { setAnalyticsConsent } from '@/lib/analytics/analytics';
import { readConsentCookie, writeConsentCookie, type ConsentState } from './consentStore';

interface ConsentContextValue {
  readonly consent: ConsentState | null;
  /** True once the cookie has been read on the client (gates banner render). */
  readonly ready: boolean;
  /** Has the visitor made a choice yet? */
  readonly decided: boolean;
  readonly manageOpen: boolean;
  readonly acceptAll: () => void;
  readonly rejectNonEssential: () => void;
  readonly setAnalytics: (on: boolean) => void;
  readonly openManage: () => void;
  readonly closeManage: () => void;
}

const ConsentContext = createContext<ConsentContextValue | null>(null);

/**
 * VRB-311 — mounts OUTSIDE `<Providers>` (in the root layout) so the banner
 * shows pre-auth, not behind the MSAL "Loading…" gate. The cookie is read on
 * the client after mount (no `cookies()` in the layout → the app stays
 * statically renderable); the banner is gated on `ready` so a returning visitor
 * who already consented never sees a flash.
 */
export const ConsentProvider = ({ children }: { readonly children: ReactNode }) => {
  const [consent, setConsent] = useState<ConsentState | null>(null);
  const [ready, setReady] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);

  useEffect(() => {
    const current = readConsentCookie();
    setConsent(current);
    setReady(true);
    setAnalyticsConsent(current?.analytics === true);
  }, []);

  const apply = useCallback((analytics: boolean) => {
    writeConsentCookie(analytics);
    setConsent(readConsentCookie());
    setAnalyticsConsent(analytics);
    setManageOpen(false);
  }, []);

  const value = useMemo<ConsentContextValue>(
    () => ({
      consent,
      ready,
      decided: consent !== null,
      manageOpen,
      acceptAll: () => apply(true),
      rejectNonEssential: () => apply(false),
      setAnalytics: (on: boolean) => apply(on),
      openManage: () => setManageOpen(true),
      closeManage: () => setManageOpen(false),
    }),
    [consent, ready, manageOpen, apply],
  );

  return <ConsentContext.Provider value={value}>{children}</ConsentContext.Provider>;
};

export const useConsent = (): ConsentContextValue => {
  const ctx = useContext(ConsentContext);
  if (!ctx) throw new Error('useConsent must be used within ConsentProvider');
  return ctx;
};
