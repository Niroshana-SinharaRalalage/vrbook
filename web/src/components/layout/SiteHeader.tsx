import Link from 'next/link';

import { SiteHeaderAuth } from './SiteHeaderAuth';
import { SiteHeaderNav } from './SiteHeaderNav';
import { SiteHeaderRoleBadge } from './SiteHeaderRoleBadge';
import { MobileNav } from './MobileNav';

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
    <>
      {/* VRB-110 — keyboard skip link: first focusable, visually hidden until
          focused, jumps past the nav to the page's <main id="main-content">. */}
      <a
        href="#main-content"
        className="sr-only rounded-md bg-background px-4 py-2 text-sm font-medium text-foreground shadow focus:not-sr-only focus:absolute focus:left-4 focus:top-3 focus:z-[60] focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
      >
        Skip to content
      </a>
      <header className="sticky top-0 z-40 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="container flex h-16 items-center justify-between">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <span className="inline-block h-6 w-6 rounded bg-brand-orange-600" aria-hidden />
          <span className="text-brand-maroon-700 dark:text-brand-orange-500">VrBook</span>
        </Link>

        {/* Desktop cluster — hidden below md, where MobileNav takes over. */}
        <div className="hidden items-center gap-4 md:flex">
          <SiteHeaderNav />
          <SiteHeaderRoleBadge />
          <SiteHeaderAuth />
        </div>

        {/* Mobile primary nav (VRB-106) — hamburger + drawer, md:hidden. */}
        <MobileNav />
      </div>
    </header>
    </>
  );
};
