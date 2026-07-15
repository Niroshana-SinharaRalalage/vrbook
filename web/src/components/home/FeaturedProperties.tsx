import { PropertyCard, type PropertyCardModel } from '@/components/property/PropertyCard';
import { searchProperties, type PropertySummary } from '@/lib/api/catalog';

/**
 * VRB-107 — real, bookable featured properties on the marketing home
 * (replaces the hardcoded placeholder, gap G17). SSR-fetched from the live
 * anonymous search API. The home page must never 500, so a fetch failure
 * falls back to a tasteful empty state (logged server-side for VRB-306
 * observability), never a broken/placeholder card.
 */

// Home:FeaturedLimit / Home:FeaturedSort — env-overridable with safe defaults.
const FEATURED_LIMIT = Number(process.env.HOME_FEATURED_LIMIT ?? 6) || 6;
const FEATURED_SORT = process.env.HOME_FEATURED_SORT ?? '-rating';

export const toCardModel = (p: PropertySummary): PropertyCardModel => ({
  slug: p.slug,
  title: p.title,
  location: `${p.city}, ${p.country}`,
  nightlyRate: p.fromNightlyRate,
  currency: p.currency,
  ratingAvg: p.averageRating,
  ratingCount: p.ratingCount,
  coverImageUrl: p.primaryImageUrl,
});

/** Fetch + map featured properties. Never throws — returns [] on any failure. */
export const loadFeatured = async (): Promise<PropertyCardModel[]> => {
  try {
    const page = await searchProperties({ sort: FEATURED_SORT, limit: FEATURED_LIMIT });
    return page.items.map(toCardModel);
  } catch (err) {
    // Home must never 500: swallow, but leave a breadcrumb for observability.
    console.error('[home] featured properties fetch failed; rendering empty state', err);
    return [];
  }
};

export const FeaturedGrid = ({ properties }: { properties: readonly PropertyCardModel[] }) => {
  if (properties.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-muted/30 px-6 py-16 text-center">
        <p className="text-base font-medium text-foreground">New stays coming soon</p>
        <p className="mt-1 text-sm text-muted-foreground">
          We&rsquo;re onboarding hosts now — check back shortly for hand-picked rentals.
        </p>
      </div>
    );
  }
  return (
    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
      {properties.map((p) => (
        <PropertyCard key={p.slug} property={p} />
      ))}
    </div>
  );
};

/** Async Server Component — the streamed featured section on the home page. */
export const FeaturedProperties = async () => {
  const properties = await loadFeatured();
  return <FeaturedGrid properties={properties} />;
};
