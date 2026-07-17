import { test, expect } from '@playwright/test';

/**
 * VRB-311 — cookie consent + legal surfaces. The #1 compliance gate: NO
 * analytics beacon fires before the visitor accepts (the story's explicit
 * rollback trigger).
 */

const isAnalyticsBeacon = (url: string): boolean =>
  url.includes('visualstudio.com') ||
  url.includes('applicationinsights') ||
  /\/v2(\.1)?\/track/.test(url);

test('no analytics beacon fires before the visitor accepts (compliance gate)', async ({ page }) => {
  const beacons: string[] = [];
  page.on('request', (r) => {
    if (isAnalyticsBeacon(r.url())) beacons.push(r.url());
  });

  await page.goto('/');
  await expect(page.getByRole('region', { name: /cookie consent/i })).toBeVisible();
  // Move around WITHOUT accepting — still nothing must leave the browser.
  await page.goto('/properties');
  await page.waitForTimeout(600);

  expect(beacons, 'no analytics beacon may fire before consent').toEqual([]);
});

test('Accept all dismisses the banner and records consent', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /accept all/i }).click();
  await expect(page.getByRole('region', { name: /cookie consent/i })).toHaveCount(0);
  const cookie = (await page.context().cookies()).find((c) => c.name === 'vrb_consent');
  expect(cookie?.value).toContain('analytics');
});

test('Reject non-essential dismisses the banner', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /reject non-essential/i }).click();
  await expect(page.getByRole('region', { name: /cookie consent/i })).toHaveCount(0);
});

test('the legal pages render with their headings', async ({ page }) => {
  const pages = [
    ['/legal/terms', 'Terms of Service'],
    ['/legal/privacy', 'Privacy Policy'],
    ['/legal/cancellation', 'Cancellation Policy'],
  ] as const;
  for (const [path, heading] of pages) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
  }
});

test('footer reaches the privacy page and re-opens cookie preferences', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /accept all/i }).click(); // clear the banner
  await page.getByRole('contentinfo').getByRole('link', { name: 'Privacy' }).click();
  await expect(page).toHaveURL(/\/legal\/privacy$/);
  await page.getByRole('button', { name: /cookie preferences/i }).click();
  await expect(page.getByRole('dialog', { name: /cookie preferences/i })).toBeVisible();
});

test.fixme('an analytics beacon fires after Accept (once DEVOPS wires the connection string)', async ({ page }) => {
  // Parked until NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING is injected
  // per-env; then a beacon to *.visualstudio.com should appear after Accept.
  await page.goto('/');
});
