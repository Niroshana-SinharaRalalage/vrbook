import type { MetadataRoute } from 'next';

import { searchProperties } from '@/lib/api/catalog';
import { SITE_URL, propertyPath } from '@/lib/seo/propertyMetadata';

/**
 * VRB-109 — dynamic sitemap generated from the live catalog (gap G34). Regenerated
 * hourly. Never throws: a catalog outage degrades to the static routes so crawlers
 * still get home + the listing page. Capped at SEO_SITEMAP_MAX_URLS (default 50k,
 * the sitemap-protocol per-file limit). Per-URL `lastModified` is omitted — the
 * catalog DTO exposes no `updatedAt` yet (flagged gap, like JSON-LD priceRange).
 */
export const revalidate = 3600;

const SITEMAP_MAX = Number(process.env.SEO_SITEMAP_MAX_URLS ?? 50000) || 50000;

const sitemap = async (): Promise<MetadataRoute.Sitemap> => {
  const staticRoutes: MetadataRoute.Sitemap = [
    { url: `${SITE_URL}/`, changeFrequency: 'daily', priority: 1 },
    { url: `${SITE_URL}/properties`, changeFrequency: 'daily', priority: 0.9 },
    // VRB-311 — legal surfaces (crawlable; low churn).
    { url: `${SITE_URL}/legal/terms`, changeFrequency: 'yearly', priority: 0.3 },
    { url: `${SITE_URL}/legal/privacy`, changeFrequency: 'yearly', priority: 0.3 },
    { url: `${SITE_URL}/legal/cancellation`, changeFrequency: 'yearly', priority: 0.3 },
  ];

  try {
    const page = await searchProperties({ limit: SITEMAP_MAX });
    const propertyRoutes: MetadataRoute.Sitemap = page.items.map((p) => ({
      url: `${SITE_URL}${propertyPath(p.slug)}`,
      changeFrequency: 'weekly',
      priority: 0.8,
    }));
    return [...staticRoutes, ...propertyRoutes];
  } catch (err) {
    console.error('[sitemap] catalog fetch failed; serving static routes only', err);
    return staticRoutes;
  }
};

export default sitemap;
