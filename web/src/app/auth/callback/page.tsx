'use client';

import { Suspense, useEffect, useRef } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { getMyTenants } from '../../../lib/tenants/useMyTenants';
import { setActiveTenantId } from '../../../lib/tenants/activeTenant';
import {
  SIGN_IN_FLOW_STORAGE_KEY,
  type SignInFlow,
} from '../../../lib/auth/msalConfig';

/**
 * Entra External ID redirect handler. MSAL's PublicClientApplication processes
 * the redirect automatically on instantiation. Once MSAL has an active account:
 *
 *   - Slice OPS.M.12.6 — inspect the `flow` carried by MSAL's `state` (or the
 *     sessionStorage fallback) to route the two surfaces differently:
 *
 *       flow='admin' → M.13.5 tenant picker (0/1/N branch below).
 *       flow='guest' → skip the picker entirely; go straight to `returnTo`.
 *                      A guest picking the wrong tenant would silently break
 *                      later mutations, so we defer picker to the point they
 *                      are promoted (a future slice).
 *
 *   - Slice OPS.M.13.5 — fetch GET /me/tenants and route by count (admin
 *     path):
 *       0 memberships → returnTo (or '/'); if PlatformAdmin, /admin/platform
 *       1 membership  → auto-set active tenant + returnTo
 *       N memberships → /select-tenant?returnTo=<encoded>
 *
 * We keep a ref-based one-shot guard so React strict-mode double-render doesn't
 * fire /me/tenants twice.
 */

interface CallbackState {
  readonly flow?: SignInFlow;
  readonly returnTo?: string;
}

const readCallbackState = (raw: string | null): CallbackState => {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (parsed && typeof parsed === 'object') {
      const flow = (parsed as { flow?: string }).flow;
      const returnTo = (parsed as { returnTo?: string }).returnTo;
      return {
        flow: flow === 'admin' ? 'admin' : flow === 'guest' ? 'guest' : undefined,
        returnTo: typeof returnTo === 'string' ? returnTo : undefined,
      };
    }
  } catch {
    // State was set by a pre-M.12.6 build (raw string return-to). Fall through
    // to sessionStorage / defaults.
  }
  return {};
};

const readSessionFlow = (): SignInFlow | undefined => {
  if (typeof window === 'undefined') return undefined;
  try {
    const stored = window.sessionStorage.getItem(SIGN_IN_FLOW_STORAGE_KEY);
    if (stored === 'admin' || stored === 'guest') return stored;
  } catch {
    // Ignore.
  }
  return undefined;
};

const CallbackInner = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { instance, inProgress } = useMsal();
  const routed = useRef(false);

  useEffect(() => {
    if (inProgress !== InteractionStatus.None) return;
    if (routed.current) return;

    const account =
      instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
    if (!account) {
      // No session — bounce to landing; the login button will re-trigger MSAL.
      router.replace('/');
      return;
    }
    instance.setActiveAccount(account);

    // MSAL surfaces the request state on ?state=<...>; fall back to search
    // params (pre-M.12.6 shape) then sessionStorage.
    const stateParam = searchParams.get('state');
    const parsed = readCallbackState(stateParam);
    const flow: SignInFlow = parsed.flow ?? readSessionFlow() ?? 'guest';
    const returnTo = parsed.returnTo ?? searchParams.get('returnTo') ?? '/';

    routed.current = true;

    if (flow === 'guest') {
      // Guest sign-in: skip the tenant picker entirely. A guest has no active
      // tenant to pick — pick would be premature; landing them at returnTo
      // matches their pre-sign-in mental model.
      router.replace(returnTo);
      return;
    }

    // Admin flow: run the M.13.5 0/1/N tenant picker branch.
    void (async () => {
      try {
        const { memberships, isPlatformAdmin } = await getMyTenants();

        if (memberships.length === 0) {
          if (isPlatformAdmin) {
            router.replace('/admin/platform');
          } else {
            router.replace(returnTo);
          }
          return;
        }

        if (memberships.length === 1 && memberships[0]) {
          setActiveTenantId(memberships[0].tenantId);
          router.replace(returnTo);
          return;
        }

        router.replace(`/select-tenant?returnTo=${encodeURIComponent(returnTo)}`);
      } catch {
        // If /me/tenants fails, fall back to the pre-M.13.5 behavior so the
        // user isn't locked out by a transient API blip.
        router.replace(returnTo);
      }
    })();
  }, [instance, inProgress, router, searchParams]);

  return (
    <main className="flex min-h-dvh items-center justify-center">
      <div className="text-center text-sm text-muted-foreground">
        Completing sign-in…
      </div>
    </main>
  );
};

const AuthCallbackPage = () => (
  <Suspense
    fallback={
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-center text-sm text-muted-foreground">Loading…</div>
      </main>
    }
  >
    <CallbackInner />
  </Suspense>
);

export default AuthCallbackPage;
