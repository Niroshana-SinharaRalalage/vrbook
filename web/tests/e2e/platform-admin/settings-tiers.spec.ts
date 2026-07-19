import { test, expect } from '../fixtures/auth.fixture';
import AxeBuilder from '@axe-core/playwright';

/**
 * VRB-216 (web) — the platform-admin Global Cancellation Tiers panel. The
 * e2e-platform-admin persona (0-membership, is_platform_admin=true) reaches the
 * platform settings surface directly.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

test('the global cancellation-tiers panel renders for a platform admin', async ({ page }) => {
  await page.goto('/admin/platform/settings/cancellation-tiers');
  await expect(page.getByRole('heading', { level: 1, name: 'Global cancellation tiers' })).toBeVisible();
  await expect(page.getByRole('spinbutton', { name: /full-refund cutoff/i })).toBeVisible();
});

test('the settings nav surfaces the now-ready tiers section', async ({ page }) => {
  await page.goto('/admin/platform/settings/cancellation-tiers');
  const nav = page.getByRole('navigation', { name: /settings sections/i });
  await expect(nav.getByRole('link', { name: 'Global cancellation tiers' })).toBeVisible();
});

test('the tiers panel has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/admin/platform/settings/cancellation-tiers');
  await expect(page.getByRole('heading', { level: 1, name: 'Global cancellation tiers' })).toBeVisible();
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  expect(violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')).toEqual([]);
});
