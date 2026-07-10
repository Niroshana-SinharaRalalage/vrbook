import path from 'node:path';

/**
 * Slice OPS.2.2 — the three Playwright E2E personas.
 *
 * Each persona signs in through REAL Entra CIAM (ADR-0016 forbids a fake-auth
 * backdoor on the admin surface). `global-setup.ts` drives one MSAL redirect
 * sign-in per persona and persists the resulting session so the authed
 * projects reuse it without re-authenticating.
 *
 * Password source: `process.env.E2E_*_PASSWORD`. CI fetches these from Key
 * Vault (`e2e-{guest,owner,platform-admin}-password`) via
 * `az keyvault secret show` and exports them into `$GITHUB_ENV`; local devs put
 * them in `web/.env.local` (gitignored) or pull from KV manually. Passwords
 * are NEVER read at module load and NEVER logged (plan §4 risks #6/#7) — they
 * are resolved lazily by `requirePassword()` only inside global-setup.
 *
 * Admin personas (owner + platform-admin) use the `admin` MSAL flow
 * (`AdminSignUpSignIn`, Entra-local only). The guest persona uses the `guest`
 * flow (`GuestSignUpSignIn`). See docs/OPS_2_PLAYWRIGHT_PLAN.md §6.
 */

export type PersonaKey = 'guest' | 'owner' | 'platform-admin';
export type MsalFlow = 'admin' | 'guest';

export interface Persona {
  /** Stable key; also the Playwright project suffix (`<key>-authed`). */
  readonly key: PersonaKey;
  /** Entra-local sign-in email. */
  readonly email: string;
  /** MSAL user flow this persona authenticates through. */
  readonly flow: MsalFlow;
  /** Env var carrying the persona's Entra-local password. */
  readonly passwordEnvVar: string;
  /** `context.storageState()` JSON — cookies + localStorage. */
  readonly storageStatePath: string;
  /**
   * sessionStorage snapshot. MSAL is configured with
   * `cacheLocation: 'sessionStorage'` (see web/src/lib/auth/msalConfig.ts), and
   * Playwright's storageState does NOT capture sessionStorage — so we persist
   * it separately and the auth fixture re-injects it via `addInitScript`.
   */
  readonly sessionStoragePath: string;
}

/**
 * Directory (relative to the web/ working dir) that holds the generated
 * session artefacts. Gitignored at repo root (`web/tests/e2e/.auth/`), pinned
 * by the OPS.2.7 arch test. Regenerated on every CI run.
 */
export const AUTH_DIR = path.join('tests', 'e2e', '.auth');

const storageStatePath = (key: PersonaKey): string =>
  path.join(AUTH_DIR, `${key}.storageState.json`);

const sessionStoragePath = (key: PersonaKey): string =>
  path.join(AUTH_DIR, `${key}.session.json`);

const persona = (
  key: PersonaKey,
  email: string,
  flow: MsalFlow,
  passwordEnvVar: string,
): Persona => ({
  key,
  email,
  flow,
  passwordEnvVar,
  storageStatePath: storageStatePath(key),
  sessionStoragePath: sessionStoragePath(key),
});

export const GUEST: Persona = persona(
  'guest',
  'e2e-guest@vrbook.test',
  'guest',
  'E2E_GUEST_PASSWORD',
);

export const OWNER: Persona = persona(
  'owner',
  'e2e-owner@vrbook.test',
  'admin',
  'E2E_OWNER_PASSWORD',
);

export const PLATFORM_ADMIN: Persona = persona(
  'platform-admin',
  'e2e-platform-admin@vrbook.test',
  'admin',
  'E2E_PLATFORM_ADMIN_PASSWORD',
);

export const PERSONAS: readonly Persona[] = [GUEST, OWNER, PLATFORM_ADMIN];

/** Maps a Playwright project name (`<key>-authed`) back to its persona. */
export const personaForProject = (projectName: string): Persona | undefined =>
  PERSONAS.find((p) => `${p.key}-authed` === projectName);

/**
 * Resolve a persona's password from the environment, or throw a clear,
 * secret-free error. Called only from global-setup — never at import time.
 */
export const requirePassword = (p: Persona): string => {
  const value = process.env[p.passwordEnvVar];
  if (!value || value === 'pending-identity-setup') {
    throw new Error(
      `E2E persona "${p.key}" password missing: set ${p.passwordEnvVar} ` +
        `(Key Vault secret e2e-${p.key}-password). See docs/OPS_2_PLAYWRIGHT_PLAN.md §6.`,
    );
  }
  return value;
};
