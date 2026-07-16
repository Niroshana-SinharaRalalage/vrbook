import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

/**
 * VRB-110 — automated axe scan of the anonymous public flows. The gate is
 * ZERO critical/serious violations (WCAG 2.2 AA tags). Home, search, and a
 * property detail page are the indexable surfaces guests hit first.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const scan = async (page: import('@playwright/test').Page) => {
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
};

test('home has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});

test('property search has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/properties');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  expect(await scan(page)).toEqual([]);
});

test('property detail has no critical/serious a11y violations', async ({ page }) => {
  await page.goto('/');
  await page
    .getByRole('main')
    .getByRole('link')
    .filter({ has: page.locator('h3') })
    .first()
    .click();
  await expect(page).toHaveURL(/\/properties\/[^/]+$/);
  expect(await scan(page)).toEqual([]);
});
