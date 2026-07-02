'use client';

import { Suspense, useEffect, useRef } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { getMyTenants } from '../../../lib/tenants/useMyTenants';
import { setActiveTenantId } from '../../../lib/tenants/activeTenant';

/**
 * Entra External ID redirect handler. MSAL's PublicClientApplication processes
 * the redirect automatically on instantiation. Once MSAL has an active account:
 *
 *   - Slice OPS.M.13.5 — fetch GET /me/tenants and route by count:
 *       0 memberships → returnTo (or '/'); if PlatformAdmin, /admin/platform
 *       1 membership  → auto-set active tenant + returnTo
 *       N memberships → /select-tenant?returnTo=<encoded>
 *
 * We keep a ref-based one-shot guard so React strict-mode double-render doesn't
 * fire /me/tenants twice.
 */
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

    const returnTo = searchParams.get('returnTo') ?? '/';
    routed.current = true;

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
