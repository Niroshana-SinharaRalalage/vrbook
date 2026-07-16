import { Suspense } from 'react';
import Link from 'next/link';
import type { Metadata } from 'next';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { FeaturedProperties } from '@/components/home/FeaturedProperties';
import { Skeleton, buttonVariants } from '@/components/ui';

// Marketing home — Server Component. VRB-107 wires the featured section to the
// live search API (see components/home/FeaturedProperties).

// VRB-109 — canonical for the home page (resolves against layout metadataBase).
export const metadata: Metadata = { alternates: { canonical: '/' } };

const FeaturedSkeleton = () => (
  <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
    {Array.from({ length: 6 }).map((_, i) => (
      <Skeleton key={i} className="aspect-[4/3] rounded-xl" />
    ))}
  </div>
);

const HomePage = () => {
  return (
    <>
      <SiteHeader />

      <main id="main-content">
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
              <Link href="/properties" className={buttonVariants({ variant: 'primary', size: 'lg' })}>
                Browse stays
              </Link>
              <Link
                href="/account/bookings"
                className={buttonVariants({ variant: 'outline', size: 'lg' })}
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
            <Link href="/properties" className="text-sm font-medium text-primary hover:underline">
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
