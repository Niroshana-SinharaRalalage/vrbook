import { test, expect } from '../fixtures/auth.fixture';

/**
 * Slice OPS.2.6 — the Platform Admin tenant console. The e2e-platform-admin
 * persona is 0-membership + is_platform_admin=true, so it reaches the
 * platform-scoped `/admin/platform/*` surface directly (no tenant picker — do
 * NOT apply the owner tenant helper here). The `e2e-tenant` (seeded, Active) is
 * the deterministic row these specs assert against.
 */

test('platform tenants list renders and shows the e2e tenant', async ({ page }) => {
  await page.goto('/admin/platform/tenants');
  await expect(page.getByRole('heading', { level: 1, name: /platform.*tenants/i })).toBeVisible();
  await expect(page.getByText(/sign in to view platform tenants/i)).toHaveCount(0);
  await expect(page.getByRole('link', { name: /e2e test tenant/i })).toBeVisible();
});

test('clicking a tenant opens its platform detail page', async ({ page }) => {
  await page.goto('/admin/platform/tenants');
  await page.getByRole('link', { name: /e2e test tenant/i }).click();
  await expect(page).toHaveURL(/\/admin\/platform\/tenants\/[0-9a-f-]+$/);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('tenant search narrows the list', async ({ page }) => {
  await page.goto('/admin/platform/tenants');
  await expect(page.getByRole('link', { name: /e2e test tenant/i })).toBeVisible();
  // A non-matching search removes the e2e tenant from the results.
  await page.getByRole('searchbox').fill('zzz-no-such-tenant');
  await expect(page.getByRole('link', { name: /e2e test tenant/i })).toHaveCount(0);
});

test('tenant status filter updates the list', async ({ page }) => {
  await page.goto('/admin/platform/tenants');
  await expect(page.getByRole('link', { name: /e2e test tenant/i })).toBeVisible();
  // e2e-tenant is Active; filtering to Suspended removes it (and must not error).
  await page.getByRole('combobox').first().selectOption('Suspended');
  await expect(page.getByRole('link', { name: /e2e test tenant/i })).toHaveCount(0);
});
