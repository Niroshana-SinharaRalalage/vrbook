import { describe, expect, it } from 'vitest';

import type { PropertyDetail } from '@/lib/api/catalog';
import { SITE_URL, buildPropertyJsonLd, buildPropertyMetadata, propertyPath } from './propertyMetadata';

const detail = (over: Partial<PropertyDetail> = {}): PropertyDetail =>
  ({
    id: 'p1',
    slug: 'sunset-villa',
    title: 'Sunset Villa',
    description: 'A '.repeat(200) + 'lovely place by the sea.',
    type: 'Villa',
    address: {
      street: '1 Ocean Rd',
      city: 'Malibu',
      state: 'CA',
      postalCode: '90265',
      countryCode: 'US',
      latitude: 34.0,
      longitude: -118.8,
    },
    maxGuests: 4,
    bedrooms: 2,
    bathrooms: 2,
    beds: 3,
    checkinFrom: '15:00',
    checkinTo: '20:00',
    checkoutBy: '11:00',
    isActive: true,
    reviewsEnabled: true,
    dynamicPricingEnabled: false,
    messagingEnabled: true,
    averageRating: 4.8,
    ratingCount: 12,
    images: [
      { id: 'i1', url: 'https://cdn/x/2.jpg', caption: null, sortOrder: 1, isPrimary: false },
      { id: 'i2', url: 'https://cdn/x/1.jpg', caption: 'Front', sortOrder: 0, isPrimary: true },
    ],
    amenities: [],
    houseRules: [],
    ...over,
  }) as PropertyDetail;

describe('propertyPath', () => {
  it('builds the detail path from the slug', () => {
    expect(propertyPath('sunset-villa')).toBe('/properties/sunset-villa');
  });
});

describe('buildPropertyMetadata', () => {
  it('sets the canonical to the property path', () => {
    expect(buildPropertyMetadata(detail()).alternates?.canonical).toBe('/properties/sunset-villa');
  });

  it('caps the description near 160 chars', () => {
    const desc = buildPropertyMetadata(detail()).description as string;
    expect(desc.length).toBeLessThanOrEqual(160);
  });

  it('emits OpenGraph with the primary image and a summary_large_image Twitter card', () => {
    const meta = buildPropertyMetadata(detail());
    const og = meta.openGraph as { images?: Array<{ url: string }>; url?: string };
    expect(og.images?.[0]?.url).toBe('https://cdn/x/1.jpg'); // primary wins
    const tw = meta.twitter as { card?: string; images?: unknown };
    expect(tw.card).toBe('summary_large_image');
    expect(tw.images).toBeTruthy();
  });

  it('omits images cleanly when the property has none', () => {
    const meta = buildPropertyMetadata(detail({ images: [] }));
    const og = meta.openGraph as { images?: unknown };
    expect(og.images).toBeUndefined();
  });
});

describe('buildPropertyJsonLd', () => {
  it('produces a LodgingBusiness with absolute url, image, address and geo', () => {
    const ld = buildPropertyJsonLd(detail());
    expect(ld['@type']).toBe('LodgingBusiness');
    expect(ld.url).toBe(`${SITE_URL}/properties/sunset-villa`);
    expect(ld.image).toEqual(expect.arrayContaining(['https://cdn/x/1.jpg']));
    expect((ld.address as { addressLocality: string }).addressLocality).toBe('Malibu');
    expect((ld.geo as { latitude: number }).latitude).toBe(34.0);
    expect((ld.aggregateRating as { reviewCount: number }).reviewCount).toBe(12);
  });

  it('does not set priceRange (PropertyDetail carries no price field)', () => {
    expect(buildPropertyJsonLd(detail())).not.toHaveProperty('priceRange');
  });

  it('omits aggregateRating when there are no ratings', () => {
    const ld = buildPropertyJsonLd(detail({ averageRating: null, ratingCount: 0 }));
    expect(ld).not.toHaveProperty('aggregateRating');
  });
});
