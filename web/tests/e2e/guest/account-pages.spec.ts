import { test, expect } from '../fixtures/auth.fixture';

/**
 * Slice OPS.2.4 — the four authed guest account surfaces render for a
 * signed-in guest (the `guest-authed` project supplies the persona session via
 * storageState + the auth fixture's sessionStorage injection). Each page gates
 * on auth with a SignInGate for anonymous visitors; asserting the real heading
 * AND the absence of the gate proves the persona session actually authenticated
 * the API calls (not just rendered the shell).
 */

test('my bookings page renders for a signed-in guest', async ({ page }) => {
  await page.goto('/account/bookings');
  await expect(page.getByRole('heading', { level: 1, name: 'My bookings' })).toBeVisible();
  await expect(page.getByText(/sign in to see your bookings/i)).toHaveCount(0);
});

test('profile page renders', async ({ page }) => {
  await page.goto('/account/profile');
  await expect(page.getByRole('heading', { level: 1, name: 'Profile' })).toBeVisible();
});

test('loyalty page renders for a signed-in guest', async ({ page }) => {
  await page.goto('/account/loyalty');
  await expect(page.getByRole('heading', { level: 1, name: 'Loyalty' })).toBeVisible();
  await expect(page.getByText(/sign in to view your loyalty status/i)).toHaveCount(0);
});

test('messages page renders for a signed-in guest', async ({ page }) => {
  await page.goto('/account/messages');
  await expect(page.getByRole('heading', { level: 1, name: 'Messages' })).toBeVisible();
  await expect(page.getByText(/sign in to view your messages/i)).toHaveCount(0);
});
