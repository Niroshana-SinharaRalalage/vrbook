import { test, expect } from '../fixtures/auth.fixture';
import AxeBuilder from '@axe-core/playwright';

/**
 * VRB-110 — automated axe scan of the signed-in guest flows (account surfaces).
 * Gate: ZERO critical/serious violations (WCAG 2.2 AA tags).
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const scan = async (page: import('@playwright/test').Page) => {
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
};

test('the profile page has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/account/profile');
  await expect(page.getByLabel(/display name/i)).toBeVisible();
  expect(await scan(page)).toEqual([]);
});

test('my-trips has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/account/bookings');
  await expect(page.getByRole('heading', { level: 1, name: 'My bookings' })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});
