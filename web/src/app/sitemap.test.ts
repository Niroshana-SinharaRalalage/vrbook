import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/api/catalog', () => ({ searchProperties: vi.fn() }));
import { searchProperties } from '@/lib/api/catalog';
import sitemap from './sitemap';

const summary = (slug: string) => ({
  id: slug,
  slug,
  title: slug,
  type: 'Villa',
  city: 'X',
  country: 'US',
  maxGuests: 2,
  bedrooms: 1,
  fromNightlyRate: 100,
  currency: 'USD',
  averageRating: null,
  ratingCount: 0,
  primaryImageUrl: null,
});

afterEach(() => vi.clearAllMocks());

describe('sitemap', () => {
  it('lists home, the listing page, and every active property URL', async () => {
    vi.mocked(searchProperties).mockResolvedValue({
      items: [summary('sunset-villa'), summary('blue-cabin')],
      nextCursor: null,
      total: 2,
    });
    const entries = await sitemap();
    const urls = entries.map((e) => e.url);
    expect(urls.some((u) => u.endsWith('/'))).toBe(true);
    expect(urls.some((u) => u.endsWith('/properties'))).toBe(true);
    expect(urls.some((u) => u.endsWith('/properties/sunset-villa'))).toBe(true);
    expect(urls.some((u) => u.endsWith('/properties/blue-cabin'))).toBe(true);
  });

  it('serves the static routes only when the catalog fetch fails (never throws)', async () => {
    vi.mocked(searchProperties).mockRejectedValue(new Error('down'));
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const entries = await sitemap();
    expect(entries.map((e) => e.url).some((u) => u.endsWith('/properties/sunset-villa'))).toBe(false);
    expect(entries.length).toBeGreaterThanOrEqual(2); // home + /properties
    expect(spy).toHaveBeenCalled();
    spy.mockRestore();
  });
});
