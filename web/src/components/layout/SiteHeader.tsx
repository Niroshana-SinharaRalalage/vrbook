import Link from 'next/link';

import { SiteHeaderAuth } from './SiteHeaderAuth';
import { SiteHeaderNav } from './SiteHeaderNav';
import { SiteHeaderRoleBadge } from './SiteHeaderRoleBadge';

/**
 * Server-rendered header shell. The nav row is delegated to
 * {@link SiteHeaderNav} (a `'use client'` component) so we can gate
 * an `Admin` link on the signed-in user's operator role via
 * `useMe()`. The rest of the header — branding + sign-in/out
 * button — stays in the server tree to minimize the JS bundle.
 *
 * <p>{@link SiteHeaderRoleBadge} sits between the nav and the sign-out
 * button so a PlatformAdmin / TenantAdmin session is visible at a
 * glance without clicking into the admin surface. Renders nothing for
 * anonymous or regular-guest sessions.</p>
 */
export const SiteHeader = () => {
  return (
    <header className="sticky top-0 z-40 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="container flex h-16 items-center justify-between">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <span className="inline-block h-6 w-6 rounded bg-brand-orange-600" aria-hidden />
          <span className="text-brand-maroon-700 dark:text-brand-orange-500">VrBook</span>
        </Link>

        <div className="flex items-center gap-4">
          <SiteHeaderNav />
          <SiteHeaderRoleBadge />
          <SiteHeaderAuth />
        </div>
      </div>
    </header>
  );
};
