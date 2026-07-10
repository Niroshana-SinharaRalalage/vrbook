import { test, expect } from '../fixtures/auth.fixture';
import { ensureAdminContext } from '../support/adminContext';
import { scopedName } from '../support/testTenant';

/**
 * Slice OPS.2.5 — property creation (§18.2 flow 1-head).
 *
 * Two robust scenarios author-and-verify now; the full multi-field happy-path
 * submit is quarantined as test.fixme until a live persona validates the form
 * selectors (the create form is a 466-line multi-section form — the one
 * genuinely-blind surface per the OPS.2.5 architect consult). The fixme carries
 * drafted selectors so OPS.2.8 is a flip-and-tune, not a from-scratch write.
 */

test.beforeEach(async ({ page }) => {
  await ensureAdminContext(page);
});

test('the new-property form is reachable from the properties list', async ({ page }) => {
  await page.goto('/admin/properties');
  await page.getByRole('link', { name: /add property/i }).first().click();
  await expect(page).toHaveURL(/\/admin\/properties\/new$/);
  await expect(page.getByLabel(/^Title/)).toBeVisible();
});

test('submitting the empty new-property form is blocked by required-field validation', async ({
  page,
}) => {
  await page.goto('/admin/properties/new');
  // Title is `required`; submitting empty triggers native HTML5 validation and
  // never navigates. No API dependency — blind-safe.
  await page.getByRole('button', { name: /save|create|publish/i }).first().click();
  await expect(page).toHaveURL(/\/admin\/properties\/new$/);
  await expect(page.getByLabel(/^Title/)).toBeVisible();
});

test.fixme(
  'owner creates a property end to end',
  // OPS.2.8: full property-create happy-path — selectors drafted from the
  // component, pending first live persona validation.
  async ({ page }) => {
    await page.goto('/admin/properties/new');

    await page.getByLabel(/^Title/).fill(scopedName('Cabin'));
    await page.getByLabel(/^Type/).selectOption('Cabin');
    await page.getByLabel(/^Description/).fill('An e2e-created listing.');
    await page.getByLabel(/^Max guests/).fill('4');
    await page.getByLabel(/^Bedrooms/).fill('2');
    await page.getByLabel(/^Beds/).fill('2');
    await page.getByLabel(/^Bathrooms/).fill('1');
    await page.getByLabel(/^Street/).fill('1 E2E Way');
    await page.getByLabel(/^City/).fill('Testville');
    await page.getByLabel(/^State/).fill('TS');
    await page.getByLabel(/^Postal code/).fill('00001');
    await page.getByLabel(/^Country code/).fill('US');
    await page.getByLabel(/^Check-in from/).fill('15:00');
    await page.getByLabel(/^Check-in until/).fill('20:00');
    await page.getByLabel(/^Check-out by/).fill('11:00');
    await page.getByLabel(/^Turnover hours/).fill('24');

    await page.getByRole('button', { name: /save|create|publish/i }).first().click();

    // Success routes back to the properties list (or the new detail); assert we
    // left the create form.
    await expect(page).not.toHaveURL(/\/admin\/properties\/new$/, { timeout: 30_000 });
  },
);
