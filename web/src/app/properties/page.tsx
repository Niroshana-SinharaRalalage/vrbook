import { Suspense } from 'react';
import type { Metadata } from 'next';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { PropertyCard, type PropertyCardModel } from '@/components/property/PropertyCard';
import { searchProperties, type PropertySummary } from '@/lib/api/catalog';

// Server-rendered search results (proposal §3.5 — SSR is revenue-critical for SEO).

export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'Browse stays',
  description: 'Search direct-booking vacation rentals.',
};

interface SearchParams {
  readonly destination?: string;
  readonly checkin?: string;
  readonly checkout?: string;
  readonly guests?: string;
  readonly amenityCodes?: string | readonly string[];
  readonly sort?: string;
}

interface PropertiesPageProps {
  readonly searchParams: SearchParams;
}

const ResultsSkeleton = () => (
  <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
    {Array.from({ length: 8 }).map((_, i) => (
      <div key={i} className="aspect-[4/3] animate-pulse rounded-xl bg-muted" aria-hidden />
    ))}
  </div>
);

const toCardModel = (p: PropertySummary): PropertyCardModel => ({
  slug: p.slug,
  title: p.title,
  location: [p.city, p.country].filter(Boolean).join(', '),
  nightlyRate: p.fromNightlyRate,
  currency: p.currency,
  ratingAvg: p.averageRating,
  ratingCount: p.ratingCount,
  coverImageUrl: p.primaryImageUrl,
});

const Results = async ({ searchParams }: { searchParams: SearchParams }) => {
  const amenityCodes = Array.isArray(searchParams.amenityCodes)
    ? searchParams.amenityCodes
    : searchParams.amenityCodes
      ? [searchParams.amenityCodes]
      : undefined;

  try {
    const page = await searchProperties({
      destination: searchParams.destination,
      checkin: searchParams.checkin,
      checkout: searchParams.checkout,
      guests: searchParams.guests ? Number(searchParams.guests) : undefined,
      amenityCodes,
      sort: searchParams.sort,
      limit: 24,
    });

    if (page.items.length === 0) {
      return (
        <div className="rounded-xl border border-dashed border-border p-12 text-center">
          <p className="text-sm text-muted-foreground">
            No properties match those filters yet. Try widening your search.
          </p>
        </div>
      );
    }
    return (
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {page.items.map((p) => (
          <PropertyCard key={p.slug} property={toCardModel(p)} />
        ))}
      </div>
    );
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Request failed';
    return (
      <div className="rounded-xl border border-dashed border-destructive/30 bg-destructive/5 p-12 text-center">
        <p className="text-sm text-destructive">Unable to load properties: {message}</p>
      </div>
    );
  }
};

const PropertiesPage = ({ searchParams }: PropertiesPageProps) => {
  return (
    <>
      <SiteHeader />
      <main className="container py-10">
        <header className="mb-8 space-y-1">
          <h1 className="text-3xl font-semibold tracking-tight">Browse stays</h1>
          <p className="text-sm text-muted-foreground">
            {searchParams.destination
              ? `Showing results for "${searchParams.destination}"`
              : 'Use the filters to narrow your search'}
          </p>
        </header>
        <Suspense fallback={<ResultsSkeleton />}>
          <Results searchParams={searchParams} />
        </Suspense>
      </main>
      <SiteFooter />
    </>
  );
};

export default PropertiesPage;
