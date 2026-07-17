import { test, expect } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';

/**
 * VRB-210 — the settings UI shell. The owner persona is a tenant-admin, so it
 * sees the tenant sections and is refused the platform area. The full
 * edit→save→audit path is fixme'd until Agent 2's VRB-215/216 endpoints land
 * (the shell is dark-launched: routes are URL-reachable but unlinked from nav).
 */

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('the settings index shows the section nav (tenant sections only)', async ({ page }) => {
  await page.goto('/admin/settings');
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  const nav = page.getByRole('navigation', { name: /settings sections/i });
  await expect(nav.getByRole('link', { name: 'Cancellation policy' })).toBeVisible();
  // platform-only section is hidden for a tenant-admin
  await expect(nav.getByRole('link', { name: 'Platform fee' })).toHaveCount(0);
});

test('a tenant-admin is refused the platform settings area (403)', async ({ page }) => {
  await page.goto('/admin/platform/settings/platform-fee');
  await expect(page.getByRole('alert')).toContainText(/platform administrators/i);
});

test('the section nav navigates to the cancellation panel', async ({ page }) => {
  await page.goto('/admin/settings');
  await page.getByRole('navigation', { name: /settings sections/i })
    .getByRole('link', { name: 'Cancellation policy' })
    .click();
  await expect(page).toHaveURL(/\/admin\/settings\/cancellation$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Cancellation policy' })).toBeVisible();
});

test('a scaffolded section shows its placeholder', async ({ page }) => {
  await page.goto('/admin/settings/pricing');
  await expect(page.getByText(/delivered in VRB-213/i)).toBeVisible();
});

test.fixme('owner edits a setting invalid→valid, saves, and sees the audit line', async ({ page }) => {
  // Full save flow — parked until Agent 2's VRB-215 cancellation endpoint +
  // VRB-211 /changes endpoint are live on staging.
  await page.goto('/admin/settings/cancellation');
});
