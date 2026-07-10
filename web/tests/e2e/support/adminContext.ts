import { type Page, expect } from '@playwright/test';
import { E2E_TENANT_SLUG } from './testTenant';

/**
 * Slice OPS.2.5 — belt-and-suspenders active-tenant guard for owner specs.
 *
 * The PRIMARY mechanism is global-setup, which establishes + captures the
 * owner's `vrbook:active-tenant` into the persona session (re-injected by the
 * auth fixture). This helper is the secondary guard: call it at the top of an
 * owner spec that lands on an admin route; if the session somehow arrives
 * without an active tenant, it drives the `/select-tenant` picker (single
 * e2e-tenant membership) and returns once on an `/admin/*` route.
 *
 * Do NOT use for the platform-admin persona — that persona has no tenant
 * membership and is platform-scoped.
 */
export const ensureAdminContext = async (page: Page): Promise<void> => {
  await page.goto('/admin');
  if (new URL(page.url()).pathname.startsWith('/select-tenant')) {
    await page.getByRole('button').filter({ hasText: E2E_TENANT_SLUG }).first().click();
  }
  await expect(page).toHaveURL(/\/admin(\/|$)/, { timeout: 30_000 });
};
