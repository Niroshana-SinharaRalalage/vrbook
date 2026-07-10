import { test, expect } from '../fixtures/auth.fixture';

/**
 * Slice OPS.2.6 — the admin-auth rejection surfaces render end to end. These
 * are the owner-locked guardrails from ADR-0016 (admin surface is Entra-local
 * only) + ADR-0017 (admins must be pre-seeded). The pages render standalone
 * (they are the destinations AdminAuthGuard / admin error boundary redirect to),
 * so navigating directly exercises the same tree the guard shows.
 */

test('admin-not-provisioned page renders (ADR-0017)', async ({ page }) => {
  await page.goto('/auth/admin-not-provisioned');
  await expect(
    page.getByRole('heading', { level: 1, name: /account hasn.t been provisioned/i }),
  ).toBeVisible();
});

test('admin-social-idp-rejected page renders (ADR-0016)', async ({ page }) => {
  await page.goto('/auth/admin-social-idp-rejected');
  await expect(
    page.getByRole('heading', { level: 1, name: /admin sign-in requires a workspace account/i }),
  ).toBeVisible();
});
