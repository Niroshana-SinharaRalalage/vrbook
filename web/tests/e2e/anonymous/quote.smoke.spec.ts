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

  // Quote panel only renders once the auto-fetch resolves — generous timeout
  // absorbs staging cold-start + burstable-PG first-query latency.
  const totalLabel = page.getByText('Total', { exact: true });
  await expect(totalLabel).toBeVisible({ timeout: 30_000 });

  // The amount sits in the sibling span; assert it carries a numeric value.
  const totalAmount = totalLabel.locator('xpath=following-sibling::span');
  await expect(totalAmount).toHaveText(/\d/);

  // Anonymous funnel: the CTA is the sign-in prompt, never "Book this stay".
  await expect(page.getByRole('button', { name: /sign in to book/i })).toBeVisible();
});
