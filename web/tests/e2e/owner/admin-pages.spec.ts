import { test, expect } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';

/**
 * Slice OPS.2.5 — the tenant-admin (owner) console surfaces render for the
 * e2e-owner persona (tenant_admin on e2e-tenant, which owns the seed property).
 * Each page gates behind AdminAuthGuard + a SignInGate; asserting the real
 * heading AND the absence of the gate proves the persona session + active
 * tenant authenticated the admin API calls.
 */

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('admin dashboard renders', async ({ page }) => {
  await page.goto('/admin');
  await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
  await expect(page.getByText(/sign in to view the admin dashboard/i)).toHaveCount(0);
});

test('properties list renders and shows the seed property', async ({ page }) => {
  await page.goto('/admin/properties');
  await expect(page.getByRole('heading', { level: 1, name: 'Properties' })).toBeVisible();
  // e2e-owner owns the seed property, so it must appear in their list.
  await expect(page.getByText('E2E Smoke Test Property').first()).toBeVisible();
});

test('bookings queue renders', async ({ page }) => {
  await page.goto('/admin/bookings');
  await expect(page.getByRole('heading', { level: 1, name: 'Bookings' })).toBeVisible();
  await expect(page.getByText(/sign in to view bookings/i)).toHaveCount(0);
});

test('calendar renders', async ({ page }) => {
  await page.goto('/admin/calendar');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('reports renders', async ({ page }) => {
  await page.goto('/admin/reports');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});
