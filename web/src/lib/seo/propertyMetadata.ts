import type { Metadata } from 'next';

import type { PropertyDetail } from '@/lib/api/catalog';

/**
 * VRB-109 — per-property SEO builders, kept out of the (CATALOG-owned) property
 * page so they're unit-testable in isolation and the page edit stays a thin
 * delegation. Absolute URLs resolve against `NEXT_PUBLIC_SITE_URL` (the same
 * env `layout.tsx` uses for `metadataBase`); param'd per env — staging value
 * for now, prod domain lands with VRB-305.
 *
 * NOTE (gaps flagged to CATALOG): `priceRange` is intentionally omitted —
 * `PropertyDetail` exposes no price (only `PropertySummary.fromNightlyRate`);
 * add it here when the detail DTO gains a price. `lastModified` on the sitemap
 * is likewise omitted (no `updatedAt` on the DTO).
 */
export const SITE_URL = (
  process.env.NEXT_PUBLIC_SITE_URL ?? 'https://www.vrbook.example.com'
).replace(/\/+$/, '');

export const propertyPath = (slug: string): string => `/properties/${slug}`;

/** Primary image first, then by sortOrder — the cover the OG/JSON-LD should lead with. */
const orderedImages = (data: PropertyDetail): readonly string[] =>
  [...data.images]
    .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.sortOrder - b.sortOrder)
    .map((i) => i.url);

export const buildPropertyMetadata = (data: PropertyDetail): Metadata => {
  const path = propertyPath(data.slug);
  const description = data.description.slice(0, 160);
  const cover = orderedImages(data)[0];

  return {
    title: data.title,
    description,
    alternates: { canonical: path },
    openGraph: {
      title: data.title,
      description: data.description.slice(0, 200),
      url: path,
      type: 'website',
      ...(cover ? { images: [{ url: cover, alt: data.title }] } : {}),
    },
    twitter: {
      card: 'summary_large_image',
      title: data.title,
      description,
      ...(cover ? { images: [cover] } : {}),
    },
  };
};

export const buildPropertyJsonLd = (data: PropertyDetail): Record<string, unknown> => {
  const images = orderedImages(data);
  return {
    '@context': 'https://schema.org',
    '@type': 'LodgingBusiness',
    name: data.title,
    description: data.description,
    url: `${SITE_URL}${propertyPath(data.slug)}`,
    ...(images.length > 0 ? { image: images } : {}),
    address: {
      '@type': 'PostalAddress',
      streetAddress: data.address.street,
      addressLocality: data.address.city,
      addressRegion: data.address.state,
      postalCode: data.address.postalCode,
      addressCountry: data.address.countryCode,
    },
    geo: {
      '@type': 'GeoCoordinates',
      latitude: data.address.latitude,
      longitude: data.address.longitude,
    },
    ...(data.averageRating !== null && data.ratingCount > 0
      ? {
          aggregateRating: {
            '@type': 'AggregateRating',
            ratingValue: data.averageRating,
            reviewCount: data.ratingCount,
          },
        }
      : {}),
  };
};
