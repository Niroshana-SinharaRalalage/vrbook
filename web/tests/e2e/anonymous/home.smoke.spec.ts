import { test, expect } from '@playwright/test';

/**
 * Slice OPS.2.3 — anonymous smoke #1: the marketing home renders as a real
 * React tree (not just HTTP 200). The curl smoke sweep already proves
 * reachability; this proves the hero + primary CTA + header actually mount.
 *
 * Data-independent: home's featured list is still a placeholder, so this never
 * depends on seeded properties.
 */
test('home renders hero, primary CTA and header @smoke', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { level: 1 })).toContainText(/hand-picked rentals/i);
  // "Browse stays" appears in both the hero and the footer — scope to the hero
  // CTA in <main> so the assertion targets exactly one element.
  await expect(
    page.getByRole('main').getByRole('link', { name: /browse stays/i }),
  ).toBeVisible();
  await expect(page.getByRole('banner')).toBeVisible();
});
