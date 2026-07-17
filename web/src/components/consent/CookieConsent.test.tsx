import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe, toHaveNoViolations } from 'jest-axe';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

import { CookieConsent } from './CookieConsent';
import { ConsentProvider } from '@/lib/consent/ConsentProvider';

const setAnalyticsConsent = vi.fn();
vi.mock('@/lib/analytics/analytics', () => ({
  setAnalyticsConsent: (v: boolean) => setAnalyticsConsent(v),
}));

expect.extend(toHaveNoViolations);

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
});

beforeEach(() => {
  document.cookie = 'vrb_consent=; path=/; max-age=0';
  setAnalyticsConsent.mockClear();
});

const renderConsent = () =>
  render(
    <ConsentProvider>
      <CookieConsent />
    </ConsentProvider>,
  );

describe('<CookieConsent /> (VRB-311)', () => {
  it('shows the banner on first visit', async () => {
    renderConsent();
    expect(await screen.findByRole('region', { name: /cookie consent/i })).toBeInTheDocument();
  });

  it('Accept all records analytics consent and hides the banner', async () => {
    const user = userEvent.setup();
    renderConsent();
    await screen.findByRole('region', { name: /cookie consent/i });
    await user.click(screen.getByRole('button', { name: /accept all/i }));
    await waitFor(() =>
      expect(screen.queryByRole('region', { name: /cookie consent/i })).not.toBeInTheDocument(),
    );
    expect(setAnalyticsConsent).toHaveBeenLastCalledWith(true);
    expect(document.cookie).toContain('vrb_consent=');
  });

  it('Reject non-essential records no analytics consent', async () => {
    const user = userEvent.setup();
    renderConsent();
    await screen.findByRole('region', { name: /cookie consent/i });
    await user.click(screen.getByRole('button', { name: /reject non-essential/i }));
    expect(setAnalyticsConsent).toHaveBeenLastCalledWith(false);
  });

  it('Manage opens a focus-trapped dialog with an analytics toggle', async () => {
    const user = userEvent.setup();
    renderConsent();
    await screen.findByRole('region', { name: /cookie consent/i });
    await user.click(screen.getByRole('button', { name: /manage/i }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveAccessibleName(/cookie preferences/i);
    await user.click(within(dialog).getByLabelText(/analytics cookies/i));
    await user.click(within(dialog).getByRole('button', { name: /save preferences/i }));
    expect(setAnalyticsConsent).toHaveBeenLastCalledWith(true);
  });

  it('the banner has no axe violations', async () => {
    const { container } = renderConsent();
    await screen.findByRole('region', { name: /cookie consent/i });
    expect(await axe(container)).toHaveNoViolations();
  });
});
