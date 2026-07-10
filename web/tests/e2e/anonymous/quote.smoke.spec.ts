import { test, expect } from '@playwright/test';
import { E2E_SMOKE_PROPERTY_SLUG } from '../support/testTenant';

/**
 * Slice OPS.2.3 — anonymous smoke #4: the unauthenticated price quote
 * calculates through the real UI. The PriceQuoteWidget auto-fetches a quote on
 * mount (default future date range; no button), hitting the `[AllowAnonymous]`
 * `POST /api/v1/properties/{id}/quotes` endpoint against the seeded property +
 * pricing plan. A logged-out visitor gets a Total and the "Sign in to book"
 * CTA — the exact anonymous funnel entry point.
 */
test('unauthenticated quote auto-calculates a total @smoke', async ({ page }) => {
  await page.goto(`/properties/${E2E_SMOKE_PROPERTY_SLUG}`);

  // The quote panel (breakdown + CTA) renders ONLY after the anonymous
  // auto-fetch resolves against [AllowAnonymous] POST /quotes. The
  // "Sign in to book" button lives inside that panel, so its visibility gates
  // on the quote succeeding AND proves the anonymous funnel (never
  // "Book this stay"). Generous timeout absorbs staging cold-start jitter.
  await expect(page.getByRole('button', { name: /sign in to book/i })).toBeVisible({
    timeout: 30_000,
  });

  // The breakdown shows a Total line.
  await expect(page.getByText('Total', { exact: true })).toBeVisible();
});
