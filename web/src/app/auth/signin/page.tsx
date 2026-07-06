'use client';

import { Suspense, useEffect, useRef } from 'react';
import { useSearchParams } from 'next/navigation';
import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import {
  loginRequestFor,
  SIGN_IN_FLOW_STORAGE_KEY,
  type SignInFlow,
} from '../../../lib/auth/msalConfig';

/**
 * Slice OPS.M.12.6 — `/auth/signin?flow={admin|guest}&returnTo=<path>` is
 * the ONLY place that kicks off `loginRedirect`. Per §6 in the plan, admin
 * surfaces (via `useAdminGuard` — M.12.7) redirect here with `?flow=admin`;
 * guest surfaces (header, sign-in gate) call `signIn({ flow: 'guest' })`
 * which hits this page indirectly via the redirect.
 *
 * We persist the flow to sessionStorage BEFORE calling `loginRedirect` so a
 * same-tab silent-refresh in the middle of the round-trip reconstructs the
 * right authority.
 */
const SignInInner = () => {
  const params = useSearchParams();
  const { instance, inProgress } = useMsal();
  const started = useRef(false);

  useEffect(() => {
    if (inProgress !== InteractionStatus.None) return;
    if (started.current) return;

    const rawFlow = (params.get('flow') ?? '').toLowerCase();
    const flow: SignInFlow = rawFlow === 'admin' ? 'admin' : 'guest';
    const returnTo = params.get('returnTo') ?? '/';

    try {
      window.sessionStorage.setItem(SIGN_IN_FLOW_STORAGE_KEY, flow);
    } catch {
      // Best-effort — the state blob in the request also carries flow.
    }

    started.current = true;
    void instance.loginRedirect(loginRequestFor(flow, returnTo));
  }, [instance, inProgress, params]);

  return (
    <main className="flex min-h-dvh items-center justify-center">
      <div className="text-center text-sm text-muted-foreground">
        Redirecting to sign-in…
      </div>
    </main>
  );
};

const SignInPage = () => (
  <Suspense
    fallback={
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-center text-sm text-muted-foreground">Loading…</div>
      </main>
    }
  >
    <SignInInner />
  </Suspense>
);

export default SignInPage;
