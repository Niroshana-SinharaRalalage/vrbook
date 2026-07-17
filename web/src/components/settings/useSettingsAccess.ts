'use client';

import { useMe } from '@/hooks/useMe';
import { useMyTenants } from '@/lib/tenants/useMyTenants';

export interface SettingsAccess {
  /** DB-authoritative platform-admin bit (post-M.21 — NOT isOwner/isAdmin). */
  readonly isPlatformAdmin: boolean;
  /** Has a `tenant_admin` membership on any tenant (ADR-0016 tenant surface). */
  readonly isTenantAdmin: boolean;
  readonly isLoading: boolean;
}

/**
 * VRB-210 — role gate for the settings surfaces (ADR-0016). Tenant sections
 * (`/admin/settings/*`) require a `tenant_admin` membership; platform sections
 * (`/admin/platform/settings/*`) require `isPlatformAdmin`. Gates ONLY on
 * `isPlatformAdmin` + membership `role` — never the legacy `isOwner`/`isAdmin`.
 */
export const useSettingsAccess = (): SettingsAccess => {
  const me = useMe();
  const tenants = useMyTenants();

  return {
    isPlatformAdmin: me.data?.isPlatformAdmin === true,
    isTenantAdmin: (tenants.data?.memberships ?? []).some((m) => m.role === 'tenant_admin'),
    isLoading: me.isLoading || tenants.isLoading,
  };
};
