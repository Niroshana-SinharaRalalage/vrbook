import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Slice OPS.M.12.6 — per-flow authority selection tests. The module reads
 * `process.env.NEXT_PUBLIC_ENTRA_AUTHORITY*` at import time, so each test
 * sets the env vars and resets modules before re-importing.
 *
 * Also pins the OPS.M.0 fix (see `docs/OPS_M_0_PLAN.md` §2.4 + §1) — the
 * apiScopes value MUST target the API app registration's exposed scope
 * (`api://vrbook/access_as_user`), NOT `${clientId}/.default`.
 */

const ADMIN_URL = 'https://vrbookcid.ciamlogin.com/tenant-guid/AdminSignUpSignIn/v2.0';
const GUEST_URL = 'https://vrbookcid.ciamlogin.com/tenant-guid/GuestSignUpSignIn/v2.0';

const env = process.env as Record<string, string | undefined>;

const clearEntraEnv = () => {
  // NEXT_PUBLIC_ENTRA_AUTHORITY (legacy single-authority) was dropped in
  // OPS.M.12.8 — no longer read; kept out of the clean-slate here to reduce
  // the risk of a stale build accidentally referencing it.
  delete env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN;
  delete env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST;
};

beforeEach(() => {
  vi.resetModules();
  clearEntraEnv();
});

afterEach(() => {
  clearEntraEnv();
});

describe('msalConfig.apiScopes', () => {
  it('requests the API exposed scope, not the SPA client id', async () => {
    const { apiScopes } = await import('./msalConfig');
    expect(apiScopes).toEqual(['api://vrbook/access_as_user', 'email']);
  });

  it('does not use the ${clientId}/.default pattern', async () => {
    const { apiScopes } = await import('./msalConfig');
    for (const scope of apiScopes) {
      expect(scope.endsWith('/.default')).toBe(false);
    }
  });
});

describe('msalConfig.authorityForFlow', () => {
  it('returns the admin-specific authority when NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN is set', async () => {
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN = ADMIN_URL;
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST = GUEST_URL;
    const { authorityForFlow } = await import('./msalConfig');
    expect(authorityForFlow('admin')).toBe(ADMIN_URL);
  });

  it('returns the guest-specific authority when NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST is set', async () => {
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN = ADMIN_URL;
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST = GUEST_URL;
    const { authorityForFlow } = await import('./msalConfig');
    expect(authorityForFlow('guest')).toBe(GUEST_URL);
  });

  it('falls through to microsoftonline common when nothing is configured (safety net)', async () => {
    const { authorityForFlow } = await import('./msalConfig');
    expect(authorityForFlow('admin')).toBe('https://login.microsoftonline.com/common');
    expect(authorityForFlow('guest')).toBe('https://login.microsoftonline.com/common');
  });

  it('resolves each flow independently — admin missing does NOT bleed into guest', async () => {
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST = GUEST_URL;
    const { authorityForFlow } = await import('./msalConfig');
    expect(authorityForFlow('admin')).toBe('https://login.microsoftonline.com/common');
    expect(authorityForFlow('guest')).toBe(GUEST_URL);
  });
});

describe('msalConfig.loginRequestFor', () => {
  it("carries the per-flow authority in the redirect request", async () => {
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN = ADMIN_URL;
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST = GUEST_URL;
    const { loginRequestFor } = await import('./msalConfig');
    expect(loginRequestFor('admin').authority).toBe(ADMIN_URL);
    expect(loginRequestFor('guest').authority).toBe(GUEST_URL);
  });

  it('encodes { flow, returnTo } as JSON in state so the callback can route without sessionStorage', async () => {
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN = ADMIN_URL;
    env.NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST = GUEST_URL;
    const { loginRequestFor } = await import('./msalConfig');
    const req = loginRequestFor('admin', '/admin/properties/42');
    expect(req.state).toBeTypeOf('string');
    const parsed = JSON.parse(req.state!);
    expect(parsed).toEqual({ flow: 'admin', returnTo: '/admin/properties/42' });
  });

  it('requests the API exposed scope regardless of flow', async () => {
    const { loginRequestFor } = await import('./msalConfig');
    expect(loginRequestFor('admin').scopes).toEqual(['api://vrbook/access_as_user', 'email']);
    expect(loginRequestFor('guest').scopes).toEqual(['api://vrbook/access_as_user', 'email']);
  });
});
