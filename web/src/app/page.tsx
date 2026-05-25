import { Suspense } from 'react';
import Link from 'next/link';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { PropertyCard, type PropertyCardModel } from '@/components/property/PropertyCard';

// Marketing home — Server Component. Featured properties are a placeholder; F1
// wires this to GET /properties?sort=-rating&limit=6.

const FeaturedSkeleton = () => (
  <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
    {Array.from({ length: 6 }).map((_, i) => (
      <div
        key={i}
        className="aspect-[4/3] animate-pulse rounded-xl bg-muted"
        aria-hidden
      />
    ))}
  </div>
);

const placeholderProperties: PropertyCardModel[] = [
  {
    slug: 'placeholder-coastal-villa',
    title: 'Featured properties land here',
    location: 'Wired by agent F1',
    nightlyRate: 0,
    currency: 'USD',
    ratingAvg: null,
    ratingCount: 0,
    coverImageUrl: null,
  },
];

const FeaturedProperties = async () => {
  // F1: replace with `await apiFetch<PagedResult<PropertyCardModel>>('/properties', { ... })`.
  return (
    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
      {placeholderProperties.map((p) => (
        <PropertyCard key={p.slug} property={p} />
      ))}
    </div>
  );
};

const HomePage = () => {
  return (
    <>
      <SiteHeader />

      <main>
        {/* Hero */}
        <section className="bg-gradient-to-br from-brand-orange-50 via-background to-background dark:from-brand-maroon-800 dark:via-background">
          <div className="container py-20 md:py-28">
            <p className="text-sm font-medium uppercase tracking-wider text-brand-orange-600">
              Direct bookings, zero service fee
            </p>
            <h1 className="mt-3 max-w-3xl text-4xl font-semibold tracking-tight text-foreground sm:text-5xl md:text-6xl">
              Stay at hand-picked rentals. <br />
              <span className="text-brand-maroon-700 dark:text-brand-orange-500">
                Book straight from the host.
              </span>
            </h1>
            <p className="mt-5 max-w-2xl text-lg text-muted-foreground">
              Skip the platform markup and message the owner directly. Same property, better price,
              real reviews.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link
                href="/properties"
                className="inline-flex h-11 items-center rounded-md bg-brand-orange-600 px-6 text-sm font-medium text-white shadow-sm transition hover:bg-brand-orange-700"
              >
                Browse stays
              </Link>
              <Link
                href="/account/bookings"
                className="inline-flex h-11 items-center rounded-md border border-border bg-background px-6 text-sm font-medium text-foreground transition hover:bg-accent"
              >
                My trips
              </Link>
            </div>
          </div>
        </section>

        {/* Featured */}
        <section className="container py-16">
          <div className="mb-8 flex items-end justify-between">
            <h2 className="text-2xl font-semibold tracking-tight">Featured stays</h2>
            <Link
              href="/properties"
              className="text-sm font-medium text-brand-orange-600 hover:underline"
            >
              See all →
            </Link>
          </div>
          <Suspense fallback={<FeaturedSkeleton />}>
            <FeaturedProperties />
          </Suspense>
        </section>
      </main>

      <SiteFooter />
    </>
  );
};

export default HomePage;
