import { type Page, type Locator } from '@playwright/test';

/**
 * Slice OPS.2.2 — thin Page Object base. Per plan §6 the POM style is
 * deliberately shallow: no deep inheritance, semantic locators preferred,
 * `data-testid` only where CSS is brittle. Concrete pages extend this for the
 * one or two locators they actually need; they do NOT wrap every element.
 */
export abstract class BasePage {
  protected constructor(protected readonly page: Page) {}

  /** App-relative path this page lives at (used by {@link goto}). */
  abstract get path(): string;

  async goto(): Promise<void> {
    await this.page.goto(this.path);
  }

  /** The site header is present on every authenticated + anonymous route. */
  get header(): Locator {
    return this.page.getByRole('banner');
  }

  /** Convenience: the primary sign-in CTA rendered by SignInGate / header. */
  get signInButton(): Locator {
    return this.page.getByRole('button', { name: /sign in/i });
  }
}
