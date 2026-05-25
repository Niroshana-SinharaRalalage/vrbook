import {
  type Configuration,
  type RedirectRequest,
  type SilentRequest,
  LogLevel,
} from '@azure/msal-browser';

/**
 * MSAL configuration for Azure AD B2C (proposal §14.1).
 *
 * Tokens are 60-min access / 90-day rolling refresh. The web app stores nothing
 * sensitive — MSAL's session storage holds the access token and we always read
 * it via `acquireTokenSilent`.
 */

const authority = process.env.NEXT_PUBLIC_B2C_AUTHORITY;
const clientId = process.env.NEXT_PUBLIC_B2C_CLIENT_ID;

const isBrowser = typeof window !== 'undefined';

const redirectUri = isBrowser ? `${window.location.origin}/auth/callback` : '/auth/callback';
const postLogoutRedirectUri = isBrowser ? `${window.location.origin}/auth/signout` : '/auth/signout';

/** Knownauthorities is required for B2C policies. */
const knownAuthorities: string[] = (() => {
  try {
    return authority ? [new URL(authority).host] : [];
  } catch {
    return [];
  }
})();

export const msalConfig: Configuration = {
  auth: {
    clientId: clientId ?? '00000000-0000-0000-0000-000000000000',
    authority: authority ?? 'https://login.microsoftonline.com/common',
    knownAuthorities,
    redirectUri,
    postLogoutRedirectUri,
    navigateToLoginRequestUrl: true,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      piiLoggingEnabled: false,
      loggerCallback: (_level, message) => {
        if (process.env.NODE_ENV !== 'production') {
          // eslint-disable-next-line no-console
          console.debug('[msal]', message);
        }
      },
    },
  },
};

/**
 * The API scope our access tokens are minted for. The B2C app registration
 * exposes an API scope of the form `https://<tenant>.onmicrosoft.com/api/access_as_user`.
 * Adjust at deploy time if the API scope differs.
 */
export const apiScopes: string[] = clientId ? [`${clientId}/.default`] : ['openid', 'profile'];

export const loginRequest: RedirectRequest = {
  scopes: apiScopes,
};

export const silentRequest: SilentRequest = {
  scopes: apiScopes,
};
