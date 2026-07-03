'use client';

import { type ReactNode, useEffect, useMemo, useState } from 'react';
import {
  PublicClientApplication,
  EventType,
  InteractionRequiredAuthError,
  type AuthenticationResult,
} from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from 'next-themes';

import { msalConfig, apiScopes } from '@/lib/auth/msalConfig';
import { setActiveTenantProvider, setTokenProvider } from '@/lib/api/client';
import { getActiveTenantId } from '@/lib/tenants/activeTenant';

interface ProvidersProps {
  readonly children: ReactNode;
}

/** Top-level client providers: react-query, MSAL (AD B2C), next-themes. */
export const Providers = ({ children }: ProvidersProps) => {
  // QueryClient must be created inside the component so it's per-tab, not per-import.
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            gcTime: 5 * 60_000,
            retry: (failureCount, error) => {
              // Don't retry on 4xx (auth, validation, etc).
              const status = (error as { status?: number } | undefined)?.status;
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

  // MSAL instance is also constructed once. SSR-safe because Providers is "use client".
  const msalInstance = useMemo(() => {
    const instance = new PublicClientApplication(msalConfig);

    instance.addEventCallback((event) => {
      if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
        const payload = event.payload as AuthenticationResult;
        if (payload.account) instance.setActiveAccount(payload.account);
      }
    });

    const existing = instance.getAllAccounts();
    if (existing.length > 0 && !instance.getActiveAccount()) {
      instance.setActiveAccount(existing[0] ?? null);
    }

    return instance;
  }, []);

  useEffect(() => {
    // Wire MSAL into the API client so apiFetch() injects bearer tokens.
    // The scope MUST be the API app registration's exposed scope (apiScopes),
    // NOT `${clientId}/.default` - the latter would target the SPA itself
    // and every authenticated /api/* call would 401 with audience mismatch.
    // See msalConfig.ts:64 + docs/OPS_M_0_PLAN.md §2.4.
    // Slice OPS.M.13.6 — attach the picker's sessionStorage value as
    // X-Active-Tenant on every non-anonymous request. Read fresh each call
    // so tab-scoped changes propagate immediately.
    setActiveTenantProvider(() => getActiveTenantId());

    setTokenProvider(async () => {
      const account = msalInstance.getActiveAccount();
      if (!account) {
        // eslint-disable-next-line no-console
        console.warn('[vrbook-auth] tokenProvider: no active MSAL account. All authed API calls will 401.');
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
            tenantId: result.tenantId,
          });
          return null;
        }
        return result.accessToken;
      } catch (err) {
        // Slice OPS.M.10.2 F11.7.4.1 — original fix redirected only on
        // InteractionRequiredAuthError. OPS.M.13.6 diagnostic pass — every
        // non-InteractionRequired failure now logs to the browser console
        // so we can see WHY the token acquisition failed. Silent null
        // returns still cause the "signed-in-but-unauthed" trap (F11.7.4.1).
        const asAny = err as { errorCode?: string; errorMessage?: string; name?: string };
        // eslint-disable-next-line no-console
        console.error('[vrbook-auth] acquireTokenSilent failed.', {
          name: asAny?.name ?? '<no-name>',
          errorCode: asAny?.errorCode ?? '<no-code>',
          errorMessage: asAny?.errorMessage ?? '<no-msg>',
          isInteractionRequired: err instanceof InteractionRequiredAuthError,
          raw: err,
        });
        if (err instanceof InteractionRequiredAuthError) {
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
  }, [msalInstance]);

  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
      <MsalProvider instance={msalInstance}>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </MsalProvider>
    </ThemeProvider>
  );
};
