import { test, expect } from '@playwright/test';

/**
 * VRB-109 — the crawler-facing SEO surface: a dynamic sitemap, a robots policy
 * that fences the private areas, and per-property canonical + JSON-LD.
 */

test('sitemap.xml is valid XML listing property URLs', async ({ request, baseURL }) => {
  const res = await request.get('/sitemap.xml');
  expect(res.status()).toBe(200);
  const body = await res.text();
  expect(body).toContain('<urlset');
  expect(body).toContain(`${baseURL}/properties`);
});

test('robots.txt disallows the private areas and points at the sitemap', async ({ request }) => {
  const res = await request.get('/robots.txt');
  expect(res.status()).toBe(200);
  const body = await res.text();
  expect(body).toMatch(/Disallow:\s*\/admin/i);
  expect(body).toMatch(/Disallow:\s*\/account/i);
  expect(body).toMatch(/Sitemap:\s*https?:\/\/\S+\/sitemap\.xml/i);
});

test('a property detail page emits a canonical link and parseable JSON-LD', async ({ page }) => {
  // Land on a real property via the home featured section (seed-independent path).
  await page.goto('/');
  await page
    .getByRole('main')
    .getByRole('link')
    .filter({ has: page.locator('h3') })
    .first()
    .click();
  await expect(page).toHaveURL(/\/properties\/[^/]+$/);

  const canonical = page.locator('link[rel="canonical"]');
  await expect(canonical).toHaveCount(1);

  const jsonLd = await page.locator('script[type="application/ld+json"]').first().textContent();
  const parsed = JSON.parse(jsonLd ?? '{}');
  expect(parsed['@type']).toBe('LodgingBusiness');
});
