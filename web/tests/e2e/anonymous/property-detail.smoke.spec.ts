import { test, expect } from '@playwright/test';
import { E2E_SMOKE_PROPERTY_SLUG } from '../support/testTenant';

/**
 * Slice OPS.2.3 — anonymous smoke #3: the property detail page renders by slug.
 * Targets the deterministic `e2e-smoke-property` seeded by
 * VrBook.Migrator.SeedE2EBackfill (is_active=true so it clears the Catalog
 * public-read RLS carve-out). Proves the SSR detail fetch + the booking widget
 * mount for an anonymous visitor.
 */
test('property detail renders by slug with booking widget @smoke', async ({ page }) => {
  await page.goto(`/properties/${E2E_SMOKE_PROPERTY_SLUG}`);

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('E2E Smoke Test Property');

  // The PriceQuoteWidget mounts with two date inputs — proof the booking
  // module rendered, not a bare 200.
  await expect(page.locator('input[type="date"]')).toHaveCount(2);
});
