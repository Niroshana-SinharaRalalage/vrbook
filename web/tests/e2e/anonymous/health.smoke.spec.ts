import { test, expect } from '@playwright/test';

/**
 * Slice OPS.2.3 — anonymous smoke #5: the web app's own health route responds
 * `{ status: 'ok' }` via the browser context. This is the same-origin
 * Next.js `/api/health` route (the Container Apps probe target), NOT the
 * backend API — so no CORS and no separate API URL.
 *
 * Uses `page.request` so it runs through the browser context per plan §1.
 */
test('web /api/health reports ok @smoke', async ({ page }) => {
  const res = await page.request.get('/api/health');
  expect(res.status()).toBe(200);

  const body = (await res.json()) as { status?: string; service?: string };
  expect(body.status).toBe('ok');
  expect(body.service).toBe('vrbook-web');
});
