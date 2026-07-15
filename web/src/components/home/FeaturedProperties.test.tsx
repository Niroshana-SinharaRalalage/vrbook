import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { PropertySummary } from '@/lib/api/catalog';
import { FeaturedGrid, loadFeatured, toCardModel } from './FeaturedProperties';

vi.mock('@/lib/api/catalog', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/catalog')>();
  return { ...actual, searchProperties: vi.fn() };
});
import { searchProperties } from '@/lib/api/catalog';

const summary = (over: Partial<PropertySummary> = {}): PropertySummary => ({
  id: 'p1',
  slug: 'sunset-villa',
  title: 'Sunset Villa',
  type: 'Villa',
  city: 'Malibu',
  country: 'US',
  maxGuests: 4,
  bedrooms: 2,
  fromNightlyRate: 220,
  currency: 'USD',
  averageRating: 4.8,
  ratingCount: 12,
  primaryImageUrl: 'https://img/x.jpg',
  ...over,
});

afterEach(() => vi.clearAllMocks());

describe('toCardModel', () => {
  it('maps a PropertySummary to a PropertyCardModel', () => {
    expect(toCardModel(summary())).toMatchObject({
      slug: 'sunset-villa',
      title: 'Sunset Villa',
      location: 'Malibu, US',
      nightlyRate: 220,
      currency: 'USD',
      ratingAvg: 4.8,
      ratingCount: 12,
      coverImageUrl: 'https://img/x.jpg',
    });
  });
});

describe('<FeaturedGrid />', () => {
  it('renders a card per property, each linking to its slug', () => {
    render(
      <FeaturedGrid
        properties={[
          toCardModel(summary()),
          toCardModel(summary({ slug: 'blue-cabin', title: 'Blue Cabin' })),
        ]}
      />,
    );
    expect(screen.getByRole('link', { name: /Sunset Villa/ })).toHaveAttribute(
      'href',
      '/properties/sunset-villa',
    );
    expect(screen.getByRole('link', { name: /Blue Cabin/ })).toHaveAttribute(
      'href',
      '/properties/blue-cabin',
    );
  });

  it('shows a tasteful empty state (no fake card) when there are no properties', () => {
    render(<FeaturedGrid properties={[]} />);
    expect(screen.getByText(/new stays coming soon/i)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('loadFeatured', () => {
  it('fetches sorted + limited featured properties and maps them', async () => {
    vi.mocked(searchProperties).mockResolvedValue({
      items: [summary()],
      nextCursor: null,
      total: 1,
    });
    const res = await loadFeatured();
    expect(searchProperties).toHaveBeenCalledWith(
      expect.objectContaining({ sort: '-rating', limit: 6 }),
    );
    expect(res).toHaveLength(1);
    expect(res[0]?.slug).toBe('sunset-villa');
  });

  it('returns empty and logs server-side on fetch error (home must never 500)', async () => {
    vi.mocked(searchProperties).mockRejectedValue(new Error('boom'));
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const res = await loadFeatured();
    expect(res).toEqual([]);
    expect(spy).toHaveBeenCalled();
    spy.mockRestore();
  });
});
