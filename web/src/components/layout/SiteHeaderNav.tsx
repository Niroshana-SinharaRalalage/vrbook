'use client';

import Link from 'next/link';
import { Search, User, Calendar, Settings } from 'lucide-react';

import { useMe } from '@/hooks/useMe';
import { cn } from '@/lib/utils/cn';

const baseLinks = [
  { href: '/properties', label: 'Stays', icon: Search },
  { href: '/account/bookings', label: 'My trips', icon: Calendar },
  { href: '/account/profile', label: 'Account', icon: User },
] as const;

/**
 * Slice OPS.M.10.2 F11.7.5.5 — client subcomponent of {@link SiteHeader}
 * that calls `useMe()` and conditionally renders the `Admin` link when
 * the signed-in user has any operator role (`isOwner`, `isAdmin`,
 * `isPlatformAdmin`).
 *
 *   - During the `useMe` loading window we render the base nav only —
 *     no skeleton, no flicker. The base nav is static so the header
 *     paints immediately; the Admin link slides in when the query lands.
 *   - The Admin entry points at `/admin` (dashboard surface) rather
 *     than `/admin/bookings` because the dashboard is the canonical
 *     operator landing per the architect's resolution of §9 (b).
 *   - Anonymous and non-operator signed-in users see exactly the base
 *     nav (no Admin entry).
 */
export const SiteHeaderNav = () => {
  const { data } = useMe();
  const isOperator = !!(data?.isOwner || data?.isAdmin || data?.isPlatformAdmin);

  return (
    <nav className="hidden items-center gap-6 md:flex" aria-label="Primary">
      {baseLinks.map(({ href, label, icon: Icon }) => (
        <Link
          key={href}
          href={href}
          className={cn(
            'flex items-center gap-1.5 text-sm text-muted-foreground transition-colors',
            'hover:text-brand-orange-600 focus-visible:text-brand-orange-600',
          )}
        >
          <Icon className="h-4 w-4" aria-hidden />
          {label}
        </Link>
      ))}
      {isOperator && (
        <Link
          href="/admin"
          className={cn(
            'flex items-center gap-1.5 text-sm text-muted-foreground transition-colors',
            'hover:text-brand-orange-600 focus-visible:text-brand-orange-600',
          )}
        >
          <Settings className="h-4 w-4" aria-hidden />
          Admin
        </Link>
      )}
    </nav>
  );
};
