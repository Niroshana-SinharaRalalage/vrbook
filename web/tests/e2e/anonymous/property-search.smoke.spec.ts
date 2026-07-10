import { test, expect } from '@playwright/test';

/**
 * Slice OPS.2.3 — anonymous smoke #2: the SSR property search page renders its
 * results shell without erroring. `/properties` is server-rendered and calls
 * the anonymous `GET /api/v1/properties`; a broken API round-trip renders the
 * "Unable to load properties" error card, which this asserts against.
 *
 * Data-independent: passes whether staging has zero rows (empty-state copy) or
 * many (property cards) — both are healthy renders. It only fails on the error
 * card or a broken tree.
 */
test('property search renders results shell without error @smoke', async ({ page }) => {
  await page.goto('/properties?destination=beach');

  await expect(page.getByRole('heading', { level: 1, name: /browse stays/i })).toBeVisible();

  // The SSR error branch renders this copy; its absence is the health signal.
  await expect(page.getByText(/unable to load properties/i)).toHaveCount(0);

  // Either a result card or the empty-state message must be present — never a
  // blank/broken main.
  const cards = page.getByRole('link', { name: /./ }).filter({ has: page.getByRole('heading', { level: 3 }) });
  const emptyState = page.getByText(/no properties match those filters/i);
  await expect(cards.first().or(emptyState.first())).toBeVisible();
});
