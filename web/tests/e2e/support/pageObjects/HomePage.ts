import { type Page, type Locator } from '@playwright/test';
import { BasePage } from './BasePage';

/**
 * Slice OPS.2.2 — first concrete POM, exercised by the OPS.2.3 anonymous smoke
 * suite (home renders + property search entry point). Kept intentionally thin.
 */
export class HomePage extends BasePage {
  constructor(page: Page) {
    super(page);
  }

  get path(): string {
    return '/';
  }

  /** Free-text destination/property search box on the landing page. */
  get searchInput(): Locator {
    return this.page.getByRole('searchbox').or(this.page.getByPlaceholder(/search/i)).first();
  }

  async search(term: string): Promise<void> {
    await this.searchInput.fill(term);
    await this.searchInput.press('Enter');
  }
}
