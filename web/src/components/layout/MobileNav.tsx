'use client';

import Link from 'next/link';
import { Menu, Search, Calendar, User, Settings, type LucideIcon } from 'lucide-react';

import { useMe } from '@/hooks/useMe';
import { useMyTenants } from '@/lib/tenants/useMyTenants';
import { cn } from '@/lib/utils/cn';
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui';
import { SiteHeaderAuth } from './SiteHeaderAuth';
import { SiteHeaderRoleBadge } from './SiteHeaderRoleBadge';

/**
 * VRB-106 — mobile primary navigation. Below `md`, the desktop
 * {@link SiteHeaderNav} is `hidden` (gap G19: the nav vanished entirely on
 * phones). This renders a hamburger that opens a right-anchored `Sheet`
 * drawer with large (≥44px) tap targets.
 *
 * The drawer is built on the shared `Sheet` primitive (Radix Dialog), so it
 * inherits focus-trap, Escape/outside-click close, and focus-return-to-the-
 * hamburger for free; the `SheetTrigger` supplies `aria-expanded`/
 * `aria-controls`. Operator visibility mirrors {@link SiteHeaderNav} exactly
 * (`useMe().isPlatformAdmin` + a `tenant_admin` membership) — no forbidden
 * `IsOwner`/`IsAdmin` literals.
 */

interface NavLink {
  readonly href: string;
  readonly label: string;
  readonly icon: LucideIcon;
}

const baseLinks: readonly NavLink[] = [
  { href: '/properties', label: 'Stays', icon: Search },
  { href: '/account/bookings', label: 'My trips', icon: Calendar },
  { href: '/account/profile', label: 'Account', icon: User },
];

const linkClass =
  'flex min-h-11 items-center gap-3 rounded-md px-3 text-base font-medium text-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background';

export const MobileNav = () => {
  const { data: me } = useMe();
  const { data: tenants } = useMyTenants();
  const hasTenantAdminMembership =
    tenants?.memberships.some((m) => m.role === 'tenant_admin') ?? false;
  const isOperator = !!(me?.isPlatformAdmin || hasTenantAdminMembership);

  return (
    <div className="md:hidden">
      <Sheet>
        <SheetTrigger
          aria-label="Menu"
          className="inline-flex h-11 w-11 items-center justify-center rounded-md text-foreground transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
        >
          <Menu className="h-6 w-6" aria-hidden="true" />
        </SheetTrigger>

        <SheetContent side="right" className="w-[80vw] max-w-xs gap-0 p-0">
          {/* Distinctive: the brand hero gradient as the drawer header. */}
          <SheetHeader className="bg-gradient-to-br from-brand-orange-50 via-background to-background p-6 dark:from-brand-maroon-800 dark:via-background">
            <SheetTitle className="text-brand-maroon-700 dark:text-brand-orange-500">
              VrBook
            </SheetTitle>
            <SiteHeaderRoleBadge />
          </SheetHeader>

          <nav aria-label="Primary" className="flex flex-col gap-1 p-4">
            {baseLinks.map(({ href, label, icon: Icon }) => (
              <SheetClose asChild key={href}>
                <Link href={href} className={linkClass}>
                  <Icon className="h-5 w-5 text-muted-foreground" aria-hidden="true" />
                  {label}
                </Link>
              </SheetClose>
            ))}
            {isOperator && (
              <SheetClose asChild>
                <Link href="/admin" className={cn(linkClass, 'text-brand-orange-700 dark:text-brand-orange-500')}>
                  <Settings className="h-5 w-5" aria-hidden="true" />
                  Admin
                </Link>
              </SheetClose>
            )}
          </nav>

          <SheetFooter className="border-t border-border p-4">
            <SiteHeaderAuth />
          </SheetFooter>
        </SheetContent>
      </Sheet>
    </div>
  );
};
