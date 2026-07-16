import { test, expect, type Page } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';

/**
 * VRB-101 — the owner photo gallery manager. A tenant-admin opens a listing,
 * uploads a photo (in-memory PNG, no disk asset), sees it in the grid, and
 * deletes it through the shared ConfirmActionModal. The full drag-reorder +
 * guest-gallery-reflects-order path is quarantined (dnd-kit keyboard drag +
 * a published, guest-visible listing) until a live persona validates it.
 */

// 1x1 transparent PNG.
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

const openFirstPropertyEditor = async (page: Page) => {
  await page.goto('/admin/properties');
  await page.locator('a[href^="/admin/properties/"]:not([href$="/new"])').first().click();
  await expect(page).toHaveURL(/\/admin\/properties\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: 'Photos' })).toBeVisible();
};

const uploadPng = (page: Page) =>
  page.locator('input[type="file"]').setInputFiles({ name: 'e2e.png', mimeType: 'image/png', buffer: PNG });

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('the photos manager renders on the listing edit page', async ({ page }) => {
  await openFirstPropertyEditor(page);
  await expect(page.getByRole('button', { name: /drag photos or browse/i })).toBeVisible();
});

test('owner uploads a photo and it appears in the gallery', async ({ page }) => {
  await openFirstPropertyEditor(page);
  const before = await page.getByRole('listitem').count();
  await uploadPng(page);
  await expect(async () => {
    expect(await page.getByRole('listitem').count()).toBeGreaterThan(before);
  }).toPass();
});

test('owner deletes a photo through the confirm modal', async ({ page }) => {
  await openFirstPropertyEditor(page);
  await uploadPng(page);
  await expect(page.getByRole('listitem').first()).toBeVisible();
  const count = await page.getByRole('listitem').count();

  await page.getByRole('listitem').first().hover();
  await page.getByRole('button', { name: /^delete photo/i }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Delete photo' }).click();

  await expect(async () => {
    expect(await page.getByRole('listitem').count()).toBeLessThan(count);
  }).toPass();
});

test.fixme('reordering updates the cover and the guest gallery reflects the new order', async ({ page }) => {
  // dnd-kit keyboard reorder (Space to lift, arrows to move) + assert the guest
  // detail page renders the gallery in the new order. Parked until a live
  // persona validates the drag interaction against a published listing.
  await openFirstPropertyEditor(page);
});
