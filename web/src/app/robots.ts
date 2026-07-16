import type { MetadataRoute } from 'next';

import { SITE_URL } from '@/lib/seo/propertyMetadata';

/**
 * VRB-109 — robots policy. Public pages are crawlable; the authenticated /
 * operator surfaces are disallowed (this is also what keeps `/admin` out of
 * the index without a cross-lane meta-tag edit). Points at the dynamic sitemap.
 */
const robots = (): MetadataRoute.Robots => ({
  rules: {
    userAgent: '*',
    allow: '/',
    disallow: ['/account', '/admin', '/auth', '/select-tenant'],
  },
  sitemap: `${SITE_URL}/sitemap.xml`,
  host: SITE_URL,
});

export default robots;
