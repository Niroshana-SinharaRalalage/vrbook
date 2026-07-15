import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

// VRB-106 mobile-checkout guardrail: pin the ≥44px tap targets on the guest
// booking widget so the 360px responsiveness can't silently regress. Layout
// only — no behaviour asserted here.

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/lib/api/pricing', () => ({ computeQuote: vi.fn() }));
vi.mock('@/lib/api/booking', () => ({ createHold: vi.fn(), placeBooking: vi.fn() }));
vi.mock('@/lib/api/catalog', () => ({ getAvailability: vi.fn().mockResolvedValue({ blocked: [] }) }));
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => ({ isAuthenticated: true, signIn: vi.fn() }) }));

import { computeQuote } from '@/lib/api/pricing';
import { PriceQuoteWidget } from './PriceQuoteWidget';

const quote = {
  nightly: [{}, {}, {}],
  subtotal: { amount: 600, currency: 'USD' },
  fees: [],
  total: { amount: 600, currency: 'USD' },
};

afterEach(() => vi.clearAllMocks());

describe('PriceQuoteWidget — mobile (360px) tap targets', () => {
  it('gives the date and guest inputs a >=44px target', () => {
    vi.mocked(computeQuote).mockResolvedValue(quote as never);
    render(<PriceQuoteWidget propertyId="p1" maxGuests={6} />);
    const dates = document.querySelectorAll('input[type="date"]');
    expect(dates).toHaveLength(2);
    dates.forEach((el) => expect(el.className).toContain('min-h-11'));
    expect(document.querySelector('input[type="number"]')?.className).toContain('min-h-11');
  });

  it('gives the Book button a full-width >=44px target once a quote loads', async () => {
    vi.mocked(computeQuote).mockResolvedValue(quote as never);
    render(<PriceQuoteWidget propertyId="p1" maxGuests={6} />);
    const book = await screen.findByRole(
      'button',
      { name: /book this stay|dates unavailable/i },
      { timeout: 3000 },
    );
    expect(book.className).toContain('min-h-11');
    expect(book.className).toContain('w-full');
  });
});
