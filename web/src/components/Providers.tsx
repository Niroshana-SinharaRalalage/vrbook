'use client';

import { type ReactNode, useEffect, useMemo, useState } from 'react';
import { PublicClientApplication, EventType, type AuthenticationResult } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from 'next-themes';

import { msalConfig } from '@/lib/auth/msalConfig';
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
    setTokenProvider(async () => {
      const account = msalInstance.getActiveAccount();
      if (!account) return null;
      try {
        const result = await msalInstance.acquireTokenSilent({
          scopes: msalConfig.auth.clientId ? [`${msalConfig.auth.clientId}/.default`] : ['openid'],
          account,
        });
        return result.accessToken;
      } catch {
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
