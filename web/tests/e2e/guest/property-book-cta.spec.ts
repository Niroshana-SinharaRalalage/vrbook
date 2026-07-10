import { test, expect } from '../fixtures/auth.fixture';
import { E2E_SMOKE_PROPERTY_SLUG } from '../support/testTenant';

/**
 * Slice OPS.2.4 — on the property detail page, an authenticated guest gets the
 * "Book this stay" CTA (the anonymous smoke asserts the mirror image: "Sign in
 * to book"). Confirms the booking widget reads the persona session.
 */
test('authed guest sees the Book this stay CTA', async ({ page }) => {
  await page.goto(`/properties/${E2E_SMOKE_PROPERTY_SLUG}`);

  // The quote panel renders after the auto-quote resolves; for a signed-in
  // guest the CTA is "Book this stay", never "Sign in to book".
  await expect(page.getByRole('button', { name: /book this stay/i })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByRole('button', { name: /sign in to book/i })).toHaveCount(0);
});
