import { Suspense } from 'react';
import type { Metadata } from 'next';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';
import { PropertyGallery } from '@/components/property/PropertyGallery';

interface PropertyDetailProps {
  readonly params: { slug: string };
}

// SSR + structured data (JSON-LD) for organic search. F1 swaps the stub for a
// real fetch of `GET /properties/{slug}` (proposal §6.2 — Catalog).
export const generateMetadata = async ({ params }: PropertyDetailProps): Promise<Metadata> => {
  return {
    title: `Property ${params.slug}`,
    description: 'Vacation rental detail — book direct with the host.',
    openGraph: {
      title: `Property ${params.slug}`,
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

const PropertyDetail = async ({ slug }: { slug: string }) => {
  // F1: const data = await apiFetch<PropertyDetailDto>(`/properties/${slug}`, { anonymous: true });

  const structuredData = {
    '@context': 'https://schema.org',
    '@type': 'LodgingBusiness',
    name: `Property ${slug}`,
    description: 'Direct-booking vacation rental.',
  };

  return (
    <>
      <script
        type="application/ld+json"
        suppressHydrationWarning
        // eslint-disable-next-line react/no-danger
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <div className="space-y-6">
        <header className="space-y-1">
          <h1 className="text-3xl font-semibold tracking-tight">Property {slug}</h1>
          <p className="text-sm text-muted-foreground">
            Detail page scaffold — wired to GET /properties/{'{slug}'} by Agent F1 (proposal §6.2).
          </p>
        </header>
        <PropertyGallery images={[]} />
        <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
          <article className="prose prose-stone dark:prose-invert lg:col-span-2">
            <p className="text-muted-foreground">
              Listing description, amenities, location, reviews, and house rules will render here.
            </p>
          </article>
          <aside className="rounded-xl border border-border bg-card p-6">
            <p className="text-sm text-muted-foreground">
              Quote &amp; booking widget — POST /bookings/holds → POST /bookings (proposal §7).
            </p>
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
          <PropertyDetail slug={params.slug} />
        </Suspense>
      </main>
      <SiteFooter />
    </>
  );
};

export default PropertyDetailPage;
