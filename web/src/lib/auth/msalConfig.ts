import {
  type Configuration,
  type RedirectRequest,
  type SilentRequest,
  LogLevel,
} from '@azure/msal-browser';

/**
 * MSAL configuration for Entra External ID (the "CIAM" tenant model).
 * See `docs/adr/0012-entra-external-id-over-b2c.md` for the provider choice
 * and `docs/OPS_M_0_PLAN.md` §2.4 for the apiScopes value.
 *
 * Tokens are 60-min access / 90-day rolling refresh. The web app stores nothing
 * sensitive — MSAL's sessionStorage holds the access token and we always read
 * it via `acquireTokenSilent`.
 */

const authority = process.env.NEXT_PUBLIC_ENTRA_AUTHORITY;
const clientId = process.env.NEXT_PUBLIC_ENTRA_CLIENT_ID;

const isBrowser = typeof window !== 'undefined';

const redirectUri = isBrowser ? `${window.location.origin}/auth/callback` : '/auth/callback';
const postLogoutRedirectUri = isBrowser ? `${window.location.origin}/auth/signout` : '/auth/signout';

/** `knownAuthorities` is required when the authority host isn't a standard
 *  Microsoft login domain. Entra External ID's `*.ciamlogin.com` qualifies. */
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
 * The API scope our access tokens are minted for. This MUST be the API app
 * registration's exposed scope (`api://vrbook/access_as_user` per
 * `docs/identity/setup.md` §3), NOT `${clientId}/.default` — the latter would
 * mint a token whose `aud` is the SPA itself, which fails the audience check
 * on every authenticated /api/* call with a 401 audience mismatch.
 */
export const apiScopes: string[] = ['api://vrbook/access_as_user'];

export const loginRequest: RedirectRequest = {
  scopes: apiScopes,
};

export const silentRequest: SilentRequest = {
  scopes: apiScopes,
};
