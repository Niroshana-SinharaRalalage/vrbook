import { test as base, expect } from '@playwright/test';
import fs from 'node:fs';
import { personaForProject } from './personas';

/**
 * Slice OPS.2.2 — authed-test harness.
 *
 * The three `*-authed` Playwright projects load their persona's cookies +
 * localStorage via `storageState` (set per-project in playwright.config.ts).
 * That is NOT enough on its own: MSAL Browser is configured with
 * `cacheLocation: 'sessionStorage'` (web/src/lib/auth/msalConfig.ts), and
 * Playwright's `storageState` captures cookies + localStorage only — never
 * sessionStorage. Without the token cache, every authenticated `/api/*` call
 * would 401 with no bearer (the exact silent-failure mode documented in
 * memory/reference_msal_browser_3x_init_pattern.md).
 *
 * This fixture closes the gap: it overrides the `context` fixture to re-inject
 * the persona's sessionStorage snapshot (captured by global-setup) via an init
 * script that runs before any page script on every navigation. Anonymous specs
 * (the `smoke` project, whose name maps to no persona) get the base context
 * untouched.
 *
 * Import `test` + `expect` from here in every authed spec instead of from
 * `@playwright/test`.
 */
export const test = base.extend({
  context: async ({ context }, use, testInfo) => {
    const persona = personaForProject(testInfo.project.name);
    if (persona && fs.existsSync(persona.sessionStoragePath)) {
      const entries = JSON.parse(
        fs.readFileSync(persona.sessionStoragePath, 'utf-8'),
      ) as ReadonlyArray<readonly [string, string]>;

      await context.addInitScript((items: ReadonlyArray<readonly [string, string]>) => {
        for (const [key, value] of items) {
          window.sessionStorage.setItem(key, value);
        }
      }, entries);
    }
    await use(context);
  },
});

export { expect };
