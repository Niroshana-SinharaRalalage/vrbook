import { test, expect } from '../fixtures/auth.fixture';

/**
 * VRB-106 — the full mobile booking funnel (quote → guest details → payment →
 * confirmation) on a 360px viewport, every control tappable with no horizontal
 * overflow.
 *
 * FIXME: parked until the payment step is live end-to-end on staging
 * (PAY VRB-102/103/104/105). The quote + tentative-book portion already carries
 * ≥44px targets (pinned by the PriceQuoteWidget mobile unit test); this
 * scenario lights up as part of the VRB-110-followup once payments land.
 */
test.use({ viewport: { width: 360, height: 780 } });

test.fixme('guest completes a booking on a 360px viewport with no overflow', async ({ page }) => {
  await page.goto('/properties');
  await page.getByRole('main').getByRole('link').filter({ has: page.locator('h3') }).first().click();
  await page.getByRole('button', { name: /book this stay|sign in to book/i }).click();
  // → guest details → Stripe Elements → confirmation (added when payments land).
  await expect(page).toHaveURL(/\/bookings\/[^/]+$/);
});
