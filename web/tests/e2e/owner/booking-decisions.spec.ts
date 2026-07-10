import { test, expect } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';
import {
  E2E_TENTATIVE_BOOKING_CONFIRM_ID,
  E2E_TENTATIVE_BOOKING_REJECT_ID,
} from '../support/testTenant';

/**
 * Slice OPS.2.5 — owner confirm / reject (§18.2 flows 2 + 3).
 *
 * Each acts on a dedicated seeded Tentative booking (SeedE2EBackfill), reset to
 * Tentative on every deploy. The action CONSUMES the booking (→ Confirmed /
 * Rejected), so on a nightly re-run without an intervening deploy the row is no
 * longer Tentative — the spec skips gracefully rather than failing (the panel
 * only renders for Tentative). The OPS.2.8 walk + the first nightly after each
 * deploy exercise the real transition.
 */

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('owner confirms a Tentative booking', async ({ page }) => {
  await page.goto(`/admin/bookings/${E2E_TENTATIVE_BOOKING_CONFIRM_ID}`);

  const awaiting = page.getByText(/awaiting your decision/i);
  const isTentative = await awaiting.isVisible().catch(() => false);
  test.skip(!isTentative, 'confirm-target booking already consumed; re-armed on next deploy');

  await page.getByRole('button', { name: /^confirm$/i }).click();
  await page.getByRole('button', { name: /confirm booking/i }).click();

  await expect(page.getByText(/^confirmed$/i)).toBeVisible({ timeout: 30_000 });
});

test('owner rejects a Tentative booking', async ({ page }) => {
  await page.goto(`/admin/bookings/${E2E_TENTATIVE_BOOKING_REJECT_ID}`);

  const awaiting = page.getByText(/awaiting your decision/i);
  const isTentative = await awaiting.isVisible().catch(() => false);
  test.skip(!isTentative, 'reject-target booking already consumed; re-armed on next deploy');

  await page.getByRole('button', { name: /^reject$/i }).click();
  // The reject modal has a reason textarea (optional) + a "Reject booking" CTA.
  await page.getByRole('button', { name: /reject booking/i }).click();

  await expect(page.getByText(/^rejected$/i)).toBeVisible({ timeout: 30_000 });
});
