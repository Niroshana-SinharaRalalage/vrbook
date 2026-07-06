'use client';

import Link from 'next/link';
import { Search, User, Calendar, Settings } from 'lucide-react';

import { useMe } from '@/hooks/useMe';
import { useMyTenants } from '@/lib/tenants/useMyTenants';
import { cn } from '@/lib/utils/cn';

const baseLinks = [
  { href: '/properties', label: 'Stays', icon: Search },
  { href: '/account/bookings', label: 'My trips', icon: Calendar },
  { href: '/account/profile', label: 'Account', icon: User },
] as const;

/**
 * Slice OPS.M.10.2 F11.7.5.5 — client subcomponent of {@link SiteHeader}
 * that conditionally renders the `Admin` link when the signed-in user
 * has any operator role.
 *
 * <p>Slice OPS.M.21 (M.15 App Roles follow-up) — the operator derivation
 * key changed. Pre-M.21 read `data?.isOwner || data?.isAdmin ||
 * data?.isPlatformAdmin` from `/api/v1/me` — the first two came from
 * `identity.users.is_owner`/`is_admin` DB columns (retired in M.21.A.3).
 * Post-M.21 the derivation keys on:</p>
 *
 * <ul>
 *   <li><c>useMe().isPlatformAdmin</c> — DB-authoritative
 *   <c>identity.users.is_platform_admin</c> flag, unchanged.</li>
 *   <li><c>useMyTenants()</c> — any active membership with
 *   <c>role="tenant_admin"</c> in <c>identity.tenant_memberships</c>.
 *   Matches the M.13.6 <c>MembershipRoles</c> shape read server-side
 *   by every handler post-M.15.</li>
 * </ul>
 *
 * <p>The Admin link points at `/admin` (dashboard surface) — the canonical
 * operator landing per the architect's resolution of §9 (b). Anonymous
 * and non-operator users see exactly the base nav.</p>
 */
export const SiteHeaderNav = () => {
  const { data: me } = useMe();
  const { data: tenants } = useMyTenants();
  const hasTenantAdminMembership = tenants?.memberships.some(m => m.role === 'tenant_admin') ?? false;
  const isOperator = !!(me?.isPlatformAdmin || hasTenantAdminMembership);

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
