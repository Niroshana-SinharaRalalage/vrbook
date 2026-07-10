import { test, expect } from '../fixtures/auth.fixture';
import { E2E_SMOKE_PROPERTY_SLUG, futureStayDates } from '../support/testTenant';

/**
 * Slice OPS.2.4 — the guest booking funnel end to end against the deterministic
 * seed property: place a Tentative booking (Phase-1 manual capture means NO
 * card entry at book time), see it in "My bookings", then cancel it. Serial so
 * the three steps share one booking; unique far-future dates keep concurrent /
 * repeat runs from colliding on the shared property's availability.
 *
 * Covers §18.2 flows: guest book (1-tail) + guest cancel.
 */
test.describe.serial('guest booking lifecycle', () => {
  const { checkin, checkout } = futureStayDates();
  let bookingUrl = '';

  test('guest places a Tentative booking', async ({ page }) => {
    await page.goto(`/properties/${E2E_SMOKE_PROPERTY_SLUG}`);

    // Set a unique future range. Fill checkin first: the default checkout then
    // falls before it (invalid) until checkout is set, which is fine — the
    // quote only re-fetches once the range is valid again.
    const dateInputs = page.locator('input[type="date"]');
    await expect(dateInputs).toHaveCount(2);
    await dateInputs.first().fill(checkin);
    await dateInputs.nth(1).fill(checkout);

    // Quote resolves → the CTA becomes actionable. Agree to house rules to
    // enable it, then book.
    await expect(page.getByRole('button', { name: /book this stay/i })).toBeVisible({
      timeout: 30_000,
    });
    await page.getByRole('checkbox').check();

    await page.getByRole('button', { name: /book this stay/i }).click();

    // Redirect to the booking detail; capture it for the cancel step.
    await page.waitForURL(/\/bookings\/[^/]+$/, { timeout: 30_000 });
    bookingUrl = new URL(page.url()).pathname;

    await expect(page.getByRole('heading', { level: 1, name: /^Booking /i })).toBeVisible();
    // Tentative renders as the "Awaiting host" status pill.
    await expect(page.getByText(/awaiting host/i)).toBeVisible();
  });

  test('the booking appears in My bookings as Tentative', async ({ page }) => {
    await page.goto('/account/bookings');
    await expect(page.getByRole('heading', { level: 1, name: 'My bookings' })).toBeVisible();
    // The list badge shows the raw status "Tentative" (detail shows the
    // friendlier "Awaiting host"). At least one must be present now.
    await expect(page.getByText('Tentative', { exact: true }).first()).toBeVisible();
  });

  test('guest cancels the Tentative booking', async ({ page }) => {
    expect(bookingUrl, 'booking was created in the first step').not.toBe('');
    await page.goto(bookingUrl);

    await page.getByRole('button', { name: /^cancel booking$/i }).click();
    // Confirm in the inline panel.
    await page.getByRole('button', { name: /confirm cancel/i }).click();

    await expect(page.getByText(/^cancelled$/i)).toBeVisible({ timeout: 30_000 });
  });
});
