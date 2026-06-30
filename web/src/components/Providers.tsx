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
import { setTokenProvider } from '@/lib/api/client';

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
    setTokenProvider(async () => {
      const account = msalInstance.getActiveAccount();
      if (!account) return null;
      try {
        const result = await msalInstance.acquireTokenSilent({
          scopes: apiScopes,
          account,
        });
        return result.accessToken;
      } catch (err) {
        // Slice OPS.M.10.2 F11.7.4.1 — don't silently return null.
        // Pre-fix any failure (consent needed, expired refresh token,
        // session expired) returned null AND let apiFetch proceed
        // without Authorization. Every authed call then 401'd
        // permanently and the user saw "Unauthorized" with no way out
        // short of manual sign-out / sign-in. InteractionRequiredAuthError
        // is the canonical signal that interactive re-auth is needed —
        // bounce to redirect so MSAL re-issues the token and brings the
        // user back to the current route.
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
