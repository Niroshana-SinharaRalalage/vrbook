/**
 * Slice OPS.M.13.6 — MSAL Browser 3.x initialization singleton.
 *
 * MSAL 3.x has a breaking change from 2.x: `PublicClientApplication` construction
 * is a two-step process. The constructor synchronously returns an uninitialized
 * instance; calling any API method (`getActiveAccount`, `handleRedirectPromise`,
 * `acquireTokenSilent`, etc.) BEFORE `await instance.initialize()` yields
 * inconsistent behavior — chiefly, `getActiveAccount()` returns null even when
 * an account exists in cache, which our token provider then interprets as
 * "unauthenticated" and omits the Authorization header. Server-side confirmation:
 * every `/api/v1/*` request from staging arrives with `HasBearer=false`.
 *
 * <p>This module wraps the instance in a module-scoped singleton with:</p>
 * <ul>
 *   <li>`msalInstance` — the singleton PCA</li>
 *   <li>`msalReady` — a promise that resolves after `initialize()` completes</li>
 *   <li>`waitForAccount(timeoutMs)` — event-driven promise that resolves when
 *       the first active account materializes; used by the token provider so
 *       the initial `/me` call in Layout doesn't race MSAL's redirect
 *       processing.</li>
 * </ul>
 */
import {
  PublicClientApplication,
  EventType,
  type AuthenticationResult,
  type AccountInfo,
} from '@azure/msal-browser';

import { msalConfig } from './msalConfig';

let msalInstanceSingleton: PublicClientApplication | null = null;
let msalReadyPromise: Promise<void> | null = null;

const getInstance = (): PublicClientApplication => {
  if (!msalInstanceSingleton) {
    msalInstanceSingleton = new PublicClientApplication(msalConfig);
    msalInstanceSingleton.addEventCallback((event) => {
      if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
        const payload = event.payload as AuthenticationResult;
        if (payload.account && msalInstanceSingleton) {
          msalInstanceSingleton.setActiveAccount(payload.account);
        }
      }
    });
  }
  return msalInstanceSingleton;
};

export const msalInstance: PublicClientApplication = getInstance();

/**
 * Resolves once MSAL Browser 3.x's `initialize()` has completed. All API
 * usage on `msalInstance` must await this promise first.
 */
export const msalReady: Promise<void> = (msalReadyPromise ??= (async () => {
  if (typeof window === 'undefined') return;
  await msalInstance.initialize();
  // Adopt an existing account if MsalProvider hasn't yet set active — covers
  // the "refresh a signed-in tab" path.
  const existing = msalInstance.getAllAccounts();
  if (existing.length > 0 && !msalInstance.getActiveAccount()) {
    msalInstance.setActiveAccount(existing[0] ?? null);
  }
})());

/**
 * Slice OPS.M.13.6 — event-driven wait for the first active account. Used by
 * the token provider on cold loads where `/me` fires immediately from
 * Layout before Entra's redirect callback has been processed.
 *
 * <p>Resolves with the account when one becomes active; resolves with `null`
 * after `timeoutMs` if no account materializes (caller should return
 * null-token → API 401 → useMe error → user can sign in explicitly).</p>
 */
export const waitForAccount = (timeoutMs = 5000): Promise<AccountInfo | null> => {
  const existing = msalInstance.getActiveAccount();
  if (existing) return Promise.resolve(existing);

  return new Promise<AccountInfo | null>((resolve) => {
    let settled = false;
    const settle = (result: AccountInfo | null) => {
      if (settled) return;
      settled = true;
      msalInstance.removeEventCallback(callbackId);
      window.clearTimeout(timeoutHandle);
      resolve(result);
    };

    const callbackId =
      msalInstance.addEventCallback((event) => {
        if (
          event.eventType === EventType.LOGIN_SUCCESS ||
          event.eventType === EventType.ACCOUNT_ADDED ||
          event.eventType === EventType.HANDLE_REDIRECT_END ||
          event.eventType === EventType.ACTIVE_ACCOUNT_CHANGED
        ) {
          const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
          if (account) settle(account);
        }
      }) ?? '';

    const timeoutHandle = window.setTimeout(() => settle(null), timeoutMs);
  });
};
