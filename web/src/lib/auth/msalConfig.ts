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
 *
 * Slice OPS.M.12.6 — flow split. Two Entra user flows back this SPA:
 * `AdminSignUpSignIn` (Entra local only) and `GuestSignUpSignIn` (Entra local
 * + Google/Microsoft/Facebook/Apple). We route to the right flow by picking a
 * per-flow authority URL. See `docs/OPS_M_12_SOCIAL_IDPS_PLAN.md` §5-§6 and
 * `docs/adr/0016-admin-vs-social-idp-surface-split.md`.
 */

export type SignInFlow = 'admin' | 'guest';

export const SIGN_IN_FLOW_STORAGE_KEY = 'vrbook-signin-flow';

const adminAuthority = process.env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN;
const guestAuthority = process.env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST;
const clientId = process.env.NEXT_PUBLIC_ENTRA_CLIENT_ID;

const isBrowser = typeof window !== 'undefined';

const redirectUri = isBrowser ? `${window.location.origin}/auth/callback` : '/auth/callback';
const postLogoutRedirectUri = isBrowser ? `${window.location.origin}/auth/signout` : '/auth/signout';

/**
 * Resolve the authority URL for a given sign-in flow. Per-flow authority is
 * the load-bearing mechanism that routes admin users through the
 * `AdminSignUpSignIn` Entra user flow (no social buttons) and guests through
 * `GuestSignUpSignIn` (all 4 socials). See §6-Q6 in the plan.
 *
 * Fallback order:
 *   1. Per-flow env var (`_ADMIN` / `_GUEST`).
 *   2. Microsoft common — safety net so MSAL init never throws (the legacy
 *      `NEXT_PUBLIC_ENTRA_AUTHORITY` single-authority fallback was dropped
 *      in M.12.8 after staging cycle 2026-07-06 confirmed the per-flow
 *      secrets are populated).
 */
export const authorityForFlow = (flow: SignInFlow): string => {
  if (flow === 'admin' && adminAuthority) return adminAuthority;
  if (flow === 'guest' && guestAuthority) return guestAuthority;
  return 'https://login.microsoftonline.com/common';
};

/** All authorities the app might use — required by MSAL's `knownAuthorities`. */
const knownAuthorities: string[] = (() => {
  const hosts = new Set<string>();
  for (const url of [adminAuthority, guestAuthority]) {
    if (!url) continue;
    try {
      hosts.add(new URL(url).host);
    } catch {
      // Ignore malformed authority — MSAL will surface a clearer error at init.
    }
  }
  return Array.from(hosts);
})();

/**
 * Base MSAL configuration. The authority here is the *guest* flow — MSAL's
 * `Configuration` requires an authority at instance-construction time, and
 * guest is the default sign-in for anonymous visitors. Per-flow authority is
 * overridden per-request via `loginRequestFor` below.
 */
export const msalConfig: Configuration = {
  auth: {
    clientId: clientId ?? '00000000-0000-0000-0000-000000000000',
    authority: authorityForFlow('guest'),
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

/**
 * Build a per-flow `RedirectRequest`. The `authority` override sends the
 * request to the correct Entra user flow; `state` is a JSON-encoded
 * `{ flow, returnTo }` blob so the callback handler routes correctly even if
 * sessionStorage was evicted mid-flow. State size is well below MSAL's 2KB
 * ceiling (see §7 risk 10 in the plan).
 */
export const loginRequestFor = (
  flow: SignInFlow,
  returnTo: string = '/',
): RedirectRequest => ({
  scopes: apiScopes,
  authority: authorityForFlow(flow),
  state: JSON.stringify({ flow, returnTo }),
});

/**
 * Legacy no-arg `loginRequest`. Retained for one-cycle backwards
 * compatibility with any callers not yet migrated to `loginRequestFor`.
 * New code MUST call `loginRequestFor(flow, returnTo)` — the flow argument
 * is what routes through the right Entra user flow. Removed in M.12.8.
 */
export const loginRequest: RedirectRequest = loginRequestFor('guest');

/**
 * Silent-token-refresh request. MSAL uses the account's cached authority
 * automatically — no per-flow override needed here, so long as the initial
 * login used the correct authority (which `loginRequestFor` guarantees).
 */
export const silentRequest: SilentRequest = {
  scopes: apiScopes,
};
