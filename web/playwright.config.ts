import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';

/**
 * Slice OPS.2 — Playwright E2E config.
 *
 * Target is DEPLOYED staging (plan §5-Q1-a), so `webServer` stays undefined
 * (§6) — CI points `PLAYWRIGHT_BASE_URL` at the staging web FQDN; local runs
 * default to localhost:3000. Chromium only (§5-Q5-a).
 *
 * Projects (§6):
 *   - `smoke`             anonymous `*.smoke.spec.ts` — no auth, blocking in CI.
 *   - `setup`             runs global-setup.ts, one real MSAL sign-in per
 *                         persona; writes tests/e2e/.auth/<persona>.storageState.json
 *                         (+ .session.json for the sessionStorage token cache).
 *   - `<persona>-authed`  depends on `setup`, reuses the persona's storageState.
 *                         The auth fixture re-injects sessionStorage (MSAL uses
 *                         cacheLocation:'sessionStorage', which storageState omits).
 */
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'html',
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'smoke',
      testMatch: /.*\.smoke\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'setup',
      testMatch: /global-setup\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'guest-authed',
      testMatch: /guest[\\/].*\.spec\.ts/,
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'tests/e2e/.auth/guest.storageState.json',
      },
    },
    {
      name: 'owner-authed',
      testMatch: /owner[\\/].*\.spec\.ts/,
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'tests/e2e/.auth/owner.storageState.json',
      },
    },
    {
      name: 'platform-admin-authed',
      testMatch: /platform-admin[\\/].*\.spec\.ts/,
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'tests/e2e/.auth/platform-admin.storageState.json',
      },
    },
  ],
});
