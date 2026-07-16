import { test, expect } from '../fixtures/auth.fixture';

/**
 * VRB-106 — on a phone-sized viewport the desktop nav is hidden and the
 * hamburger drawer takes over (gap G19). A signed-in guest opens it and
 * navigates. Focus-trap / Escape / return-focus are covered by the MobileNav
 * unit tests; this proves the real end-to-end open + navigate on mobile.
 */
test.use({ viewport: { width: 390, height: 844 } });

test('guest opens the mobile drawer and sees the primary nav', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: /menu/i })).toBeVisible();
  await page.getByRole('button', { name: /menu/i }).click();
  const drawer = page.getByRole('dialog');
  await expect(drawer.getByRole('link', { name: 'Stays' })).toBeVisible();
  await expect(drawer.getByRole('link', { name: 'My trips' })).toBeVisible();
});

test('selecting a drawer link navigates and closes the drawer', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /menu/i }).click();
  await page.getByRole('dialog').getByRole('link', { name: 'My trips' }).click();
  await expect(page).toHaveURL(/\/account\/bookings$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);
});
