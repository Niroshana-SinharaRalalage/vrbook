import { test, expect } from '../fixtures/auth.fixture';

/**
 * VRB-108 — a signed-in guest edits their display name, saves (PUT /me), and
 * the change persists across a reload. Restores the original name afterwards so
 * the shared persona is left unchanged.
 */
test('guest edits and persists their display name', async ({ page }) => {
  await page.goto('/account/profile');

  const name = page.getByLabel(/display name/i);
  await expect(name).toBeVisible();
  const original = await name.inputValue();
  const next = `E2E Guest ${Date.now() % 100000}`;

  await name.fill(next);
  await page.getByRole('button', { name: /save/i }).click();
  await expect(page.getByRole('status')).toHaveText(/saved/i);

  await page.reload();
  await expect(page.getByLabel(/display name/i)).toHaveValue(next);

  // Restore so the persona is unchanged for other specs.
  await page.getByLabel(/display name/i).fill(original);
  await page.getByRole('button', { name: /save/i }).click();
  await expect(page.getByRole('status')).toHaveText(/saved/i);
});
