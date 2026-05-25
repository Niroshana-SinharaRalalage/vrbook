import { Suspense } from 'react';
import type { Metadata } from 'next';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { PropertyCard, type PropertyCardModel } from '@/components/property/PropertyCard';

// Server-rendered search results (proposal §3.5 — SSR is revenue-critical for SEO).

export const metadata: Metadata = {
  title: 'Browse stays',
  description: 'Search direct-booking vacation rentals.',
};

interface SearchParams {
  readonly destination?: string;
  readonly checkin?: string;
  readonly checkout?: string;
  readonly guests?: string;
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

const Results = async ({ searchParams: _searchParams }: { searchParams: SearchParams }) => {
  // F1: wire to `apiFetch<PagedResult<PropertyCardModel>>('/properties', { query: ... })`.
  const stub: PropertyCardModel[] = [];
  if (stub.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-12 text-center">
        <p className="text-sm text-muted-foreground">
          Search wiring lands here — implemented by Agent F1 (proposal §6.2 — Catalog: GET /properties).
        </p>
      </div>
    );
  }
  return (
    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {stub.map((p) => (
        <PropertyCard key={p.slug} property={p} />
      ))}
    </div>
  );
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
