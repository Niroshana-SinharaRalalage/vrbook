'use client';

import { useCallback, useMemo } from 'react';
import {
  useAccount,
  useIsAuthenticated,
  useMsal,
} from '@azure/msal-react';
import { InteractionStatus, type AccountInfo } from '@azure/msal-browser';
import { loginRequest, silentRequest } from './msalConfig';

export interface AuthUser {
  readonly oid: string;
  readonly email: string | undefined;
  readonly name: string | undefined;
  readonly isOwner: boolean;
  readonly isAdmin: boolean;
}

const toAuthUser = (account: AccountInfo | null): AuthUser | null => {
  if (!account) return null;
  const claims = (account.idTokenClaims ?? {}) as Record<string, unknown>;
  return {
    oid: account.localAccountId,
    email: (claims.email as string | undefined) ?? account.username,
    name: account.name,
    isOwner: claims.extension_isOwner === true || claims.extension_isOwner === 'true',
    isAdmin: claims.extension_isAdmin === true || claims.extension_isAdmin === 'true',
  };
};

/**
 * Single-call auth surface used by the rest of the app. Wraps MSAL so callers
 * don't import the SDK directly.
 */
export const useAuth = () => {
  const { instance, accounts, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const account = useAccount(accounts[0] ?? null);

  const user = useMemo(() => toAuthUser(account), [account]);

  const signIn = useCallback(() => {
    void instance.loginRedirect(loginRequest);
  }, [instance]);

  const signOut = useCallback(() => {
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
