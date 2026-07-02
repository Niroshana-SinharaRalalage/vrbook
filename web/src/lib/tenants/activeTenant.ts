/**
 * Slice OPS.M.13.5 — client-side active-tenant state.
 *
 * Storage: sessionStorage. Per-tab (each open tab may have a different
 * active tenant). Cleared on sign-out. Never persists across browser
 * restarts (design decision per OPS.M.13 §3.2 — deep-link into a specific
 * tenant means signing in fresh; no cross-tab tenant leakage).
 *
 * The value is written by /select-tenant (or the auth callback for the
 * single-membership auto-pick case) and read by the api client to attach
 * X-Active-Tenant on every request (added in M.13.6).
 */

const KEY = 'vrbook:active-tenant';

const isBrowser = (): boolean => typeof window !== 'undefined';

export const getActiveTenantId = (): string | null => {
  if (!isBrowser()) return null;
  try {
    return window.sessionStorage.getItem(KEY);
  } catch {
    // sessionStorage disabled / quota / privacy mode
    return null;
  }
};

export const setActiveTenantId = (tenantId: string): void => {
  if (!isBrowser()) return;
  try {
    window.sessionStorage.setItem(KEY, tenantId);
  } catch {
    /* no-op on storage failure — request will just fall back to no header */
  }
};

export const clearActiveTenantId = (): void => {
  if (!isBrowser()) return;
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {
    /* no-op */
  }
};
