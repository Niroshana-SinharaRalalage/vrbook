'use client';

import { useCallback, useMemo } from 'react';
import {
  useAccount,
  useIsAuthenticated,
  useMsal,
} from '@azure/msal-react';
import { InteractionStatus, type AccountInfo } from '@azure/msal-browser';
import {
  loginRequestFor,
  silentRequest,
  type SignInFlow,
  SIGN_IN_FLOW_STORAGE_KEY,
} from './msalConfig';
import { clearActiveTenantId } from '../tenants/activeTenant';

export interface AuthUser {
  readonly oid: string;
  readonly email: string | undefined;
  readonly name: string | undefined;
}

export interface SignInOptions {
  readonly flow?: SignInFlow;
  readonly returnTo?: string;
}

const toAuthUser = (account: AccountInfo | null): AuthUser | null => {
  if (!account) return null;
  const claims = (account.idTokenClaims ?? {}) as Record<string, unknown>;
  // Slice OPS.M.15.6 — the legacy `extension_isOwner` / `extension_isAdmin`
  // token claims were retired backend-side in M.15.2/M.15.5. The SPA no
  // longer reads them; nav derivation reads `/api/v1/me`'s `isOwner`/
  // `isAdmin` DTO fields (kept for one cycle per M.15 §7-Q1) via
  // `useMe`/`useMyTenants`, NOT the id-token claims. See ADR-0014
  // amendment + docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md.
  return {
    oid: account.localAccountId,
    email: (claims.email as string | undefined) ?? account.username,
    name: account.name,
  };
};

/**
 * Single-call auth surface used by the rest of the app. Wraps MSAL so callers
 * don't import the SDK directly.
 *
 * Slice OPS.M.12.6 — `signIn` accepts an optional `{ flow }` argument to
 * select the Entra user flow (admin vs guest). Defaults to `guest` because
 * that's the anonymous-visitor case; admin surfaces pass `{ flow: 'admin' }`
 * explicitly via `useAdminGuard` (see M.12.7).
 */
export const useAuth = () => {
  const { instance, accounts, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const account = useAccount(accounts[0]);

  const user = useMemo(() => toAuthUser(account), [account]);

  const signIn = useCallback(
    (options?: SignInOptions) => {
      const flow: SignInFlow = options?.flow ?? 'guest';
      const returnTo = options?.returnTo ?? (typeof window !== 'undefined' ? window.location.pathname : '/');
      // Persist the flow so a same-tab silent-refresh reconstructs the right
      // authority (§6.2 in the plan). MSAL's cache reads the account's
      // recorded authority, but the flow name is what our callback handler
      // reads to route between admin picker vs guest returnTo.
      if (typeof window !== 'undefined') {
        try {
          window.sessionStorage.setItem(SIGN_IN_FLOW_STORAGE_KEY, flow);
        } catch {
          // sessionStorage unavailable (private browsing edge) — fall through;
          // the state blob in the request still carries the flow.
        }
      }
      void instance.loginRedirect(loginRequestFor(flow, returnTo));
    },
    [instance],
  );

  const signOut = useCallback(() => {
    // Slice OPS.M.13.7 — clear the per-tab active tenant BEFORE MSAL's
    // redirect so a subsequent sign-in in the same tab doesn't inherit
    // the previous account's picked workspace (would silently show
    // "wrong" data or trigger a Cross-tenant write rejected 403 on the
    // first mutation).
    clearActiveTenantId();
    // Slice OPS.M.12.6 — clear the sign-in flow too so a re-sign-in
    // starts clean.
    if (typeof window !== 'undefined') {
      try {
        window.sessionStorage.removeItem(SIGN_IN_FLOW_STORAGE_KEY);
      } catch {
        // Ignore — best-effort cleanup.
      }
    }
    void instance.logoutRedirect();
  }, [instance]);

  /**
   * Acquire an API access token. Falls back to interactive redirect if the
   * silent flow can't satisfy the request (consent needed, expired, etc).
   */
  const getAccessToken = useCallback(async (): Promise<string | null> => {
    if (!account) return null;
    try {
      const result = await instance.acquireTokenSilent({ ...silentRequest, account });
      return result.accessToken;
    } catch {
      await instance.acquireTokenRedirect({ ...silentRequest, account });
      return null;
    }
  }, [instance, account]);

  return {
    isAuthenticated,
    isBusy: inProgress !== InteractionStatus.None,
    user,
    signIn,
    signOut,
    getAccessToken,
  };
};
