'use client';

import { type ReactNode, useEffect, useState } from 'react';
import { EventType, InteractionRequiredAuthError } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from 'next-themes';

import { apiScopes } from '@/lib/auth/msalConfig';
import { msalInstance, msalReady, waitForAccount } from '@/lib/auth/msalInstance';
import { setActiveTenantProvider, setTokenProvider } from '@/lib/api/client';
import { getActiveTenantId } from '@/lib/tenants/activeTenant';

interface ProvidersProps {
  readonly children: ReactNode;
}

/**
 * MSAL 3.x error codes that mean "the silent path can't work — the user must
 * interact." Superset of `InteractionRequiredAuthError` — architect review of
 * OPS.M.13.6 walk debug identified several BrowserAuthError / ClientAuthError
 * codes that also require redirect but aren't the specific instanceof type.
 */
const INTERACTION_REQUIRED_CODES = new Set([
  'no_account_error',
  'no_tokens_found',
  'token_refresh_required',
  'consent_required',
  'login_required',
  'interaction_required',
]);

const isInteractionNeeded = (err: unknown): boolean => {
  if (err instanceof InteractionRequiredAuthError) return true;
  const code = (err as { errorCode?: string })?.errorCode ?? '';
  return INTERACTION_REQUIRED_CODES.has(code);
};

/**
 * Top-level client providers: react-query, MSAL (Entra External ID), next-themes.
 *
 * <p>Slice OPS.M.13.6 — gated on `msalReady` so MSAL Browser 3.x's `initialize()`
 * completes before `<MsalProvider>` mounts and before the token provider is
 * wired. Without this gate, `useMe` fires on first render, `getActiveAccount()`
 * returns null (MSAL not initialized), no Authorization header is sent, and
 * every authed request 401s — verified via LA `HasBearer=false` on every
 * `/api/v1/*` challenge before this fix.</p>
 */
export const Providers = ({ children }: ProvidersProps) => {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            gcTime: 5 * 60_000,
            retry: (failureCount, error) => {
              // 4xx generally isn't retryable, but a single 401 retry gives the
              // MSAL init race one chance to self-heal (token provider had to
              // wait for account materialization on cold load).
              const status = (error as { status?: number } | undefined)?.status;
              if (status === 401 && failureCount < 1) return true;
              if (status && status >= 400 && status < 500) return false;
              return failureCount < 2;
            },
            refetchOnWindowFocus: false,
          },
          mutations: {
            retry: false,
          },
        },
      }),
  );

  const [msalReadyState, setMsalReadyState] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void msalReady.then(() => {
      if (!cancelled) setMsalReadyState(true);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!msalReadyState) return;

    // Slice OPS.M.13.6 — active tenant provider reads sessionStorage fresh
    // on every request so tab-scoped picker changes propagate immediately.
    setActiveTenantProvider(() => getActiveTenantId());

    setTokenProvider(async () => {
      // Belt-and-braces — msalReady already resolved by the time this callback
      // is registered, but await again in case module gets reloaded.
      await msalReady;

      let account = msalInstance.getActiveAccount();
      if (!account) {
        // Cold-load race: /me can fire from Layout before Entra's redirect
        // has been processed. Wait up to 5s for the account event.
        account = await waitForAccount(5000);
      }

      if (!account) {
        // eslint-disable-next-line no-console
        console.warn('[vrbook-auth] tokenProvider: no active MSAL account after wait. Callers get 401 and useMe retries once.');
        return null;
      }

      try {
        const result = await msalInstance.acquireTokenSilent({
          scopes: apiScopes,
          account,
        });
        if (!result.accessToken) {
          // eslint-disable-next-line no-console
          console.warn('[vrbook-auth] acquireTokenSilent returned an empty accessToken.', {
            scopes: apiScopes,
            expiresOn: result.expiresOn,
          });
          return null;
        }
        return result.accessToken;
      } catch (err) {
        const asAny = err as { errorCode?: string; errorMessage?: string; name?: string };
        // eslint-disable-next-line no-console
        console.error('[vrbook-auth] acquireTokenSilent failed.', {
          name: asAny?.name ?? '<no-name>',
          errorCode: asAny?.errorCode ?? '<no-code>',
          errorMessage: asAny?.errorMessage ?? '<no-msg>',
          interactionNeeded: isInteractionNeeded(err),
        });
        if (isInteractionNeeded(err)) {
          try {
            await msalInstance.acquireTokenRedirect({
              scopes: apiScopes,
              account,
            });
          } catch {
            // Redirect kicks off a navigation; nothing else to do.
          }
        }
        return null;
      }
    });

    // Slice OPS.M.13.6 — invalidate cached error results when MSAL account
    // materializes post-mount so `useMe`'s initial 401 self-heals without a
    // user-visible sign-in prompt.
    const callbackId = msalInstance.addEventCallback((event) => {
      if (
        event.eventType === EventType.LOGIN_SUCCESS ||
        event.eventType === EventType.ACCOUNT_ADDED ||
        event.eventType === EventType.HANDLE_REDIRECT_END ||
        event.eventType === EventType.ACTIVE_ACCOUNT_CHANGED
      ) {
        void queryClient.invalidateQueries({ queryKey: ['me'] });
        void queryClient.resetQueries({
          predicate: (q) => q.state.status === 'error',
        });
      }
    });

    return () => {
      if (callbackId) msalInstance.removeEventCallback(callbackId);
    };
  }, [msalReadyState, queryClient]);

  if (!msalReadyState) {
    return (
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-sm text-muted-foreground">Loading…</div>
      </main>
    );
  }

  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
      <MsalProvider instance={msalInstance}>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </MsalProvider>
    </ThemeProvider>
  );
};
