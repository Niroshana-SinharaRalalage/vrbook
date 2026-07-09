'use client';

import { Building2, Crown } from 'lucide-react';

import { useMe } from '@/hooks/useMe';
import { useMyTenants } from '@/lib/tenants/useMyTenants';
import { cn } from '@/lib/utils/cn';

/**
 * A chip in the site header indicating the signed-in user's operator role.
 * Three states:
 *
 * <ul>
 *   <li><b>PlatformAdmin</b> (loudest): solid brand-orange chip
 *   "Platform Admin".</li>
 *   <li><b>Any active <c>tenant_admin</c> membership</b>: outlined
 *   maroon chip "Tenant Admin — {displayName}". Picks the primary
 *   membership first, else the first found.</li>
 *   <li><b>Neither</b> (regular guest, anonymous, query loading):
 *   renders nothing.</li>
 * </ul>
 *
 * <p>Data sources match {@link SiteHeaderNav} (ADR-0014 post-M.21):
 * {@link useMe} <c>isPlatformAdmin</c> reads
 * <c>identity.users.is_platform_admin</c>; {@link useMyTenants}
 * memberships filtered by role. No legacy DTO reads.</p>
 *
 * <p>PlatformAdmin takes priority even when the user ALSO has
 * <c>tenant_admin</c> memberships — cross-tenant operator authority is
 * the more relevant signal to surface.</p>
 */
export const SiteHeaderRoleBadge = () => {
  const { data: me } = useMe();
  const { data: tenants } = useMyTenants();

  if (me?.isPlatformAdmin) {
    return (
      <span
        className={cn(
          'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5',
          'text-xs font-semibold text-white',
          'bg-brand-orange-600',
        )}
        aria-label="Signed in as Platform Admin"
        data-testid="role-badge-platform-admin"
      >
        <Crown className="h-3 w-3" aria-hidden />
        Platform Admin
      </span>
    );
  }

  const tenantAdmin =
    tenants?.memberships.find((m) => m.role === 'tenant_admin' && m.isPrimary) ??
    tenants?.memberships.find((m) => m.role === 'tenant_admin');

  if (tenantAdmin) {
    return (
      <span
        className={cn(
          'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5',
          'text-xs font-semibold',
          'border border-brand-maroon-300 text-brand-maroon-700',
          'bg-brand-maroon-50',
          'dark:border-brand-maroon-700 dark:text-brand-orange-500 dark:bg-brand-maroon-950/40',
        )}
        aria-label={`Signed in as Tenant Admin of ${tenantAdmin.displayName}`}
        data-testid="role-badge-tenant-admin"
      >
        <Building2 className="h-3 w-3" aria-hidden />
        <span>Tenant Admin — {tenantAdmin.displayName}</span>
      </span>
    );
  }

  return null;
};
