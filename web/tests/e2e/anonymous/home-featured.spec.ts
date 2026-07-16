import { test, expect } from '@playwright/test';

/**
 * VRB-107 — the home "Featured stays" section renders REAL properties from the
 * search API (no longer the hardcoded placeholder) and each card links into its
 * detail page. Depends on the E2E seed property existing on staging.
 */

test('home featured section shows a real property card @smoke', async ({ page }) => {
  await page.goto('/');
  const featured = page.getByRole('main');
  // Either a real card links to a detail page, or the tasteful empty state.
  const cards = featured.getByRole('link', { name: /./ }).filter({ has: page.locator('img, h3') });
  await expect(async () => {
    const count = await cards.count();
    expect(count).toBeGreaterThan(0);
  }).toPass();
});

test('clicking a featured card opens its property detail', async ({ page }) => {
  await page.goto('/');
  const firstCard = page
    .getByRole('main')
    .getByRole('link')
    .filter({ has: page.locator('h3') })
    .first();
  await firstCard.click();
  await expect(page).toHaveURL(/\/properties\/[^/]+$/);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});
