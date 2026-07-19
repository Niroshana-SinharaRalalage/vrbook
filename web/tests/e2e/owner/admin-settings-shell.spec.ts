import { test, expect } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';

/**
 * VRB-210 shell + shell-integration pass. The owner persona is a tenant-admin,
 * so it sees the tenant sections and is refused the platform area. The nav is
 * re-linked and the audit trail renders against the live /changes endpoint; the
 * full edit→save path stays fixme'd until Agent 2's cancellation-model endpoint
 * lands on staging.
 */

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('the settings nav shows only READY sections (placeholders + platform hidden)', async ({ page }) => {
  await page.goto('/admin/settings');
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  const nav = page.getByRole('navigation', { name: /settings sections/i });
  await expect(nav.getByRole('link', { name: 'Cancellation policy' })).toBeVisible();
  // not-yet-built placeholder sections are gated out of the nav (pre-prod hide)
  await expect(nav.getByRole('link', { name: 'Pricing & fees' })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Availability' })).toHaveCount(0);
  // platform-only section hidden for a tenant-admin
  await expect(nav.getByRole('link', { name: 'Platform fee' })).toHaveCount(0);
});

test('a gated placeholder section is still reachable by direct URL (unlinked, not removed)', async ({ page }) => {
  await page.goto('/admin/settings/pricing');
  await expect(page.getByText(/delivered in VRB-213/i)).toBeVisible();
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

test('the Settings area is reachable from the admin sidebar (re-linked)', async ({ page }) => {
  await page.goto('/admin');
  await page.getByRole('navigation').getByRole('link', { name: 'Settings' }).first().click();
  await expect(page).toHaveURL(/\/admin\/settings$/);
});

test('the recent-changes audit panel renders against the live /changes endpoint (VRB-211)', async ({ page }) => {
  await page.goto('/admin/settings/cancellation');
  // The audit panel calls GET /admin/settings/changes (live). It must render
  // its card (empty state or history) — NOT the "unavailable" error state.
  await expect(page.getByRole('heading', { name: 'Recent changes' })).toBeVisible();
  await expect(page.getByText(/change history is currently unavailable/i)).toHaveCount(0);
});

test.fixme('owner edits a setting invalid→valid, saves, and sees the audit line', async ({ page }) => {
  // Full save flow — parked until Agent 2's VRB-215 cancellation endpoint +
  // VRB-211 /changes endpoint are live on staging.
  await page.goto('/admin/settings/cancellation');
});
