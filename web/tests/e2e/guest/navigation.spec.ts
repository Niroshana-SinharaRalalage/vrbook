import { test, expect } from '../fixtures/auth.fixture';

/**
 * Slice OPS.2.4 — a signed-in guest sees the authed header nav ("My trips" +
 * "Account") and never a "Sign in" CTA, and can navigate from the header into
 * their trips. Proves the header derives the authed state from the persona
 * session.
 */

test('header shows authed guest nav, not a sign-in CTA', async ({ page }) => {
  await page.goto('/');
  const header = page.getByRole('banner');
  await expect(header.getByRole('link', { name: /my trips/i })).toBeVisible();
  await expect(header.getByRole('button', { name: /^sign in$/i })).toHaveCount(0);
});

test('guest navigates from header to My trips', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('banner').getByRole('link', { name: /my trips/i }).click();
  await expect(page).toHaveURL(/\/account\/bookings$/);
  await expect(page.getByRole('heading', { level: 1, name: 'My bookings' })).toBeVisible();
});
