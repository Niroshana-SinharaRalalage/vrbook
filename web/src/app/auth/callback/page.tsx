'use client';

import { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';

/**
 * Entra External ID redirect handler. MSAL's PublicClientApplication processes
 * the redirect automatically on instantiation — this page exists so the
 * configured `redirectUri` resolves to a real route, and so we can route the
 * user to wherever they were headed when auth was triggered.
 */
const CallbackInner = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { instance, inProgress } = useMsal();

  useEffect(() => {
    if (inProgress !== InteractionStatus.None) return;
    const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
    if (account) instance.setActiveAccount(account);

    const returnTo = searchParams.get('returnTo') ?? '/';
    router.replace(returnTo);
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
