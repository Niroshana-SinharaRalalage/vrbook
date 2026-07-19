import { test as setup, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {
  AUTH_DIR,
  GUEST,
  OWNER,
  PLATFORM_ADMIN,
  requirePassword,
  type Persona,
} from './fixtures/personas';
import { E2E_TENANT_SLUG } from './support/testTenant';

/**
 * Slice OPS.2.2 — authentication setup, run as the `setup` Playwright project.
 * Every `*-authed` project declares `dependencies: ['setup']`, so these three
 * sign-ins run once per invocation before any authed spec.
 *
 * Per plan §5-Q2-a the PRIMARY strategy is a REAL MSAL redirect sign-in through
 * staging Entra CIAM — no fake-auth backdoor (ADR-0016). Each persona:
 *   1. hits `/auth/signin?flow={admin|guest}` which fires MSAL `loginRedirect`;
 *   2. completes the Entra-local email + password form on the hosted CIAM page;
 *   3. lands back on the app authenticated; we then persist BOTH
 *      `storageState` (cookies + localStorage) AND the sessionStorage snapshot
 *      (MSAL's `cacheLocation: 'sessionStorage'` token cache — see
 *      web/src/lib/auth/msalConfig.ts). The auth fixture re-injects the latter.
 *
 * FALLBACK (plan §5-Q2-b, NOT used unless the redirect proves un-drivable):
 * msal-node `acquireTokenByUsernamePassword` (ROPC). If the CIAM hosted-page
 * selectors below drift, that is the OPS.2.2 discovery to escalate — document
 * in docs/runbooks/playwright-e2e-flake.md rather than loosening auth.
 *
 * NOTE: these tests only run for the authed projects (nightly, informational
 * during the OPS.2 landing window per §5-Q4-a). They fail loud — with a
 * secret-free message — when a persona password is absent, so "personas not yet
 * provisioned in Entra + KV" surfaces as an obvious red rather than a mystery.
 */

const ensureAuthDir = (): void => {
  fs.mkdirSync(AUTH_DIR, { recursive: true });
};

/**
 * Drive the Entra External ID (CIAM) hosted sign-in page. Selectors mirror the
 * AAD/B2C combined-flow markup (`loginfmt` / `passwd` / `idSIButton9`); kept
 * resilient with role/label fallbacks. First live run validates these against
 * the actual staging CIAM user flow (operator walk, OPS.2.8 §7 checklist).
 */
const completeEntraSignIn = async (page: Page, email: string, password: string): Promise<void> => {
  // Entra External ID (CIAM) hosted sign-in uses a newer UI than classic
  // AAD/B2C (a labelled "Email address" textbox + "Next", then a password step),
  // so locators are role/placeholder-first for CIAM with the legacy
  // `loginfmt`/`idSIButton9` markup kept as fallbacks (OPS.2.8 first-live-run).
  const emailField = page
    .getByRole('textbox', { name: /email/i })
    .or(page.getByPlaceholder(/email/i))
    .or(page.locator('input[type="email"], input[name="loginfmt"]'))
    .first();
  await emailField.waitFor({ state: 'visible', timeout: 30_000 });
  await emailField.fill(email);
  await page
    .getByRole('button', { name: /next|continue|sign in/i })
    .or(page.locator('#idSIButton9, input[type="submit"], button[type="submit"]'))
    .first()
    .click();

  const passwordField = page
    .locator('input[type="password"], input[name="passwd"]')
    .first();
  await passwordField.waitFor({ state: 'visible', timeout: 30_000 });
  await passwordField.fill(password);
  await page
    .getByRole('button', { name: /sign in|next|submit|continue/i })
    .or(page.locator('#idSIButton9, input[type="submit"], button[type="submit"]'))
    .first()
    .click();

  // Optional "Stay signed in?" (KMSI) interstitial. Answer "No" — the captured
  // session is enough for the run and we avoid a long-lived persistent cookie.
  const staySignedInNo = page
    .getByRole('button', { name: /^no$/i })
    .or(page.locator('#idBtn_Back'))
    .first();
  try {
    await staySignedInNo.waitFor({ state: 'visible', timeout: 5_000 });
    await staySignedInNo.click();
  } catch {
    // KMSI not shown for this flow — normal, continue.
  }
};

/**
 * Wait until MSAL has written its token cache into sessionStorage. MSAL keys
 * are namespaced by client id; we just need at least one entry that looks like
 * an access/id token record.
 */
const waitForMsalSession = async (page: Page): Promise<void> => {
  await expect
    .poll(
      async () =>
        page.evaluate(() =>
          Object.keys(window.sessionStorage).some(
            (k) => k.includes('login.windows') || k.includes('msal') || k.includes('accesstoken'),
          ),
        ),
      { timeout: 30_000, message: 'MSAL never populated sessionStorage after sign-in' },
    )
    .toBe(true);
};

/**
 * OPS.2.5 — ensure the owner's active tenant (`vrbook:active-tenant`) is set in
 * sessionStorage before the session snapshot. Exercises the real
 * single-membership auto-select; falls through the tenant picker if needed.
 */
const establishOwnerTenantContext = async (page: Page): Promise<void> => {
  await page.goto('/admin');
  // If auto-select hasn't landed, the admin guard bounces to the picker.
  if (new URL(page.url()).pathname.startsWith('/select-tenant')) {
    await page.getByRole('button').filter({ hasText: E2E_TENANT_SLUG }).first().click();
    await page.waitForURL((url) => url.pathname.startsWith('/admin'), { timeout: 30_000 });
  }
  await expect
    .poll(() => page.evaluate(() => window.sessionStorage.getItem('vrbook:active-tenant')), {
      timeout: 30_000,
      message: 'active tenant never established for the owner persona',
    })
    .not.toBeNull();
};

const authenticate = async (page: Page, persona: Persona): Promise<void> => {
  const password = requirePassword(persona);
  ensureAuthDir();

  await page.goto(`/auth/signin?flow=${persona.flow}&returnTo=/`);
  await completeEntraSignIn(page, persona.email, password);

  // Redirect chain lands back on the app origin (via /auth/callback).
  await page.waitForURL((url) => url.pathname.startsWith('/') && !url.pathname.startsWith('/auth/signin'), {
    timeout: 45_000,
  });
  await waitForMsalSession(page);

  // OPS.2.5 — owner persona: establish + confirm the active tenant BEFORE
  // snapshotting. The single-membership auto-select in /auth/callback writes
  // `vrbook:active-tenant` to sessionStorage ASYNCHRONOUSLY (after getMyTenants),
  // which can race the capture below. Drive /admin, fall through /select-tenant
  // if the auto-select hasn't landed, and poll until the key exists so the
  // captured session reliably carries tenant context. Platform-admin is
  // 0-membership (routes to /admin/platform, no active tenant) — skip it.
  if (persona.key === 'owner') {
    await establishOwnerTenantContext(page);
  }

  // Persist cookies + localStorage.
  await page.context().storageState({ path: persona.storageStatePath });

  // Persist the sessionStorage token cache separately (storageState omits it).
  const sessionEntries = await page.evaluate(() =>
    Object.entries(window.sessionStorage) as [string, string][],
  );
  fs.writeFileSync(persona.sessionStoragePath, JSON.stringify(sessionEntries), 'utf-8');
};

setup.describe.configure({ mode: 'parallel' });

setup(`authenticate guest → ${path.basename(GUEST.storageStatePath)}`, async ({ page }) => {
  await authenticate(page, GUEST);
});

setup(`authenticate owner → ${path.basename(OWNER.storageStatePath)}`, async ({ page }) => {
  await authenticate(page, OWNER);
});

setup(
  `authenticate platform-admin → ${path.basename(PLATFORM_ADMIN.storageStatePath)}`,
  async ({ page }) => {
    await authenticate(page, PLATFORM_ADMIN);
  },
);
