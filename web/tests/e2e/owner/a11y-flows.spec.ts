import { test, expect, type Page } from '../fixtures/auth.fixture';
import AxeBuilder from '@axe-core/playwright';
import { ensureAdminContext } from '../support/adminContext';

/**
 * VRB-110-followup — axe scan of the now-shipped owner surfaces (property photo
 * gallery, settings shell, cancellation panel). Gate: ZERO critical/serious
 * violations (WCAG 2.2 AA tags).
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const scan = async (page: Page) => {
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
};

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('the property edit page (photo gallery) has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/admin/properties');
  await page.locator('a[href^="/admin/properties/"]:not([href$="/new"])').first().click();
  await expect(page).toHaveURL(/\/admin\/properties\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: 'Photos' })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});

test('the settings shell has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/admin/settings');
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});

test('the cancellation policy panel has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/admin/settings/cancellation');
  await expect(page.getByRole('heading', { level: 1, name: 'Cancellation policy' })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});
