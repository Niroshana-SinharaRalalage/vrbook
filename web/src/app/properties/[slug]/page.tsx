import { Suspense } from 'react';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { Star, MapPin, Users, BedDouble, Bath, Clock } from 'lucide-react';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { PropertyGallery } from '@/components/property/PropertyGallery';
import { PriceQuoteWidget } from '@/components/property/PriceQuoteWidget';
import { ApiProblemError } from '@/lib/api/client';
import { getPropertyBySlug, type PropertyDetail } from '@/lib/api/catalog';

interface PropertyDetailProps {
  readonly params: { slug: string };
}

export const dynamic = 'force-dynamic';

const safeFetch = async (slug: string): Promise<PropertyDetail | null> => {
  try {
    return await getPropertyBySlug(slug);
  } catch (err) {
    if (err instanceof ApiProblemError && err.status === 404) {
      return null;
    }
    throw err;
  }
};

export const generateMetadata = async ({ params }: PropertyDetailProps): Promise<Metadata> => {
  const data = await safeFetch(params.slug);
  if (!data) {
    return { title: 'Property not found' };
  }
  return {
    title: data.title,
    description: data.description.slice(0, 160),
    openGraph: {
      title: data.title,
      description: data.description.slice(0, 200),
      type: 'website',
    },
  };
};

const DetailSkeleton = () => (
  <div className="space-y-6">
    <div className="h-8 w-1/2 animate-pulse rounded bg-muted" />
    <div className="aspect-[2/1] animate-pulse rounded-xl bg-muted" />
    <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
      <div className="md:col-span-2 space-y-3">
        <div className="h-4 w-3/4 animate-pulse rounded bg-muted" />
        <div className="h-4 w-2/3 animate-pulse rounded bg-muted" />
        <div className="h-4 w-full animate-pulse rounded bg-muted" />
      </div>
      <div className="h-64 animate-pulse rounded-xl bg-muted" />
    </div>
  </div>
);

const PropertyDetailView = async ({ slug }: { slug: string }) => {
  const data = await safeFetch(slug);
  if (!data) {
    notFound();
  }

  const structuredData = {
    '@context': 'https://schema.org',
    '@type': 'LodgingBusiness',
    name: data.title,
    description: data.description,
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

  const galleryImages = data.images.map((i) => ({
    url: i.url,
    alt: i.caption ?? data.title,
  }));

  // Group amenities by category for display.
  type AmenityRow = (typeof data.amenities)[number];
  const amenitiesByCategory: Record<string, AmenityRow[]> = {};
  for (const a of data.amenities) {
    (amenitiesByCategory[a.category] ??= []).push(a);
  }

  return (
    <>
      <script
        type="application/ld+json"
        suppressHydrationWarning
        // eslint-disable-next-line react/no-danger
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <div className="space-y-6">
        <header className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">{data.title}</h1>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-muted-foreground">
            {data.averageRating !== null && data.ratingCount > 0 && (
              <span className="inline-flex items-center gap-1">
                <Star className="h-4 w-4 fill-current text-brand-orange-500" aria-hidden />
                {data.averageRating.toFixed(1)}
                <span className="text-muted-foreground/70">({data.ratingCount})</span>
              </span>
            )}
            <span className="inline-flex items-center gap-1">
              <MapPin className="h-4 w-4" aria-hidden />
              {data.address.city}, {data.address.countryCode}
            </span>
            <span className="inline-flex items-center gap-1">{data.type}</span>
          </div>
        </header>

        <PropertyGallery images={galleryImages} />

        <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
          <article className="lg:col-span-2 space-y-8">
            <section>
              <h2 className="mb-3 text-xl font-semibold tracking-tight">About this place</h2>
              <p className="whitespace-pre-line text-sm leading-relaxed text-foreground/90">{data.description}</p>
            </section>

            <section>
              <h2 className="mb-3 text-xl font-semibold tracking-tight">What this place offers</h2>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <div className="flex items-center gap-2 text-sm">
                  <Users className="h-5 w-5 text-muted-foreground" aria-hidden />
                  <span>{data.maxGuests} guests</span>
                </div>
                <div className="flex items-center gap-2 text-sm">
                  <BedDouble className="h-5 w-5 text-muted-foreground" aria-hidden />
                  <span>
                    {data.bedrooms} bedroom{data.bedrooms === 1 ? '' : 's'} ({data.beds} beds)
                  </span>
                </div>
                <div className="flex items-center gap-2 text-sm">
                  <Bath className="h-5 w-5 text-muted-foreground" aria-hidden />
                  <span>
                    {data.bathrooms} bath{data.bathrooms === 1 ? '' : 's'}
                  </span>
                </div>
                <div className="flex items-center gap-2 text-sm">
                  <Clock className="h-5 w-5 text-muted-foreground" aria-hidden />
                  <span>
                    Check-in {data.checkinFrom.slice(0, 5)}–{data.checkinTo.slice(0, 5)}
                  </span>
                </div>
              </div>
            </section>

            {Object.keys(amenitiesByCategory).length > 0 && (
              <section>
                <h2 className="mb-3 text-xl font-semibold tracking-tight">Amenities</h2>
                <div className="space-y-4">
                  {Object.entries(amenitiesByCategory).map(([category, items]) => (
                    <div key={category}>
                      <h3 className="mb-2 text-sm font-medium text-muted-foreground">{category}</h3>
                      <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                        {items.map((a) => (
                          <li key={a.id} className="text-sm">
                            • {a.name}
                          </li>
                        ))}
                      </ul>
                    </div>
                  ))}
                </div>
              </section>
            )}

            {data.houseRules.length > 0 && (
              <section>
                <h2 className="mb-3 text-xl font-semibold tracking-tight">House rules</h2>
                <ul className="space-y-1 text-sm">
                  {data.houseRules.map((rule) => (
                    <li key={rule}>• {rule}</li>
                  ))}
                </ul>
              </section>
            )}
          </article>

          <aside className="rounded-xl border border-border bg-card p-6 lg:sticky lg:top-24 lg:self-start">
            <PriceQuoteWidget propertyId={data.id} maxGuests={data.maxGuests} />
          </aside>
        </div>
      </div>
    </>
  );
};

const PropertyDetailPage = ({ params }: PropertyDetailProps) => {
  return (
    <>
      <SiteHeader />
      <main className="container py-10">
        <Suspense fallback={<DetailSkeleton />}>
          <PropertyDetailView slug={params.slug} />
        </Suspense>
      </main>
      <SiteFooter />
    </>
  );
};

export default PropertyDetailPage;
