/**
 * Slice OPS.M.8 §4 — typed client for the PlatformAdmin operator surface.
 *
 * Cross-tenant safety contract (OPS.M.8 §7): the route segment {tenantId}
 * here is the TARGET tenant (not the caller's own tenant). Safe ONLY
 * because every endpoint is gated by [Authorize(Roles="PlatformAdmin")]
 * on the server. The web client may pass any tenant id from the list page
 * URL; the backend's role gate + handler defense-in-depth do the
 * filtering.
 */
import { apiFetch } from './client';

export interface PlatformTenantListItem {
  readonly id: string;
  readonly slug: string;
  readonly displayName: string;
  readonly status: 'PendingOnboarding' | 'Active' | 'Suspended' | 'Closed';
  readonly hasStripeAccount: boolean;
  readonly chargesEnabled: boolean;
  readonly payoutsEnabled: boolean;
  readonly defaultCurrency: string;
  readonly platformFeeBps: number;
  readonly createdAt: string;
}

export interface PlatformTenantListResponse {
  readonly items: readonly PlatformTenantListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface PlatformTenant {
  readonly id: string;
  readonly slug: string;
  readonly displayName: string;
  readonly status: 'PendingOnboarding' | 'Active' | 'Suspended' | 'Closed';
  readonly suspendedReason: string | null;
  readonly defaultCurrency: string;
  readonly platformFeeBps: number;
  readonly stripeAccountStatus: string | null;
  readonly chargesEnabled: boolean;
  readonly payoutsEnabled: boolean;
  readonly hasStripeAccount: boolean;
  readonly propertyCount: number;
  readonly activeBookingCount: number;
  readonly totalBookingCount: number;
  readonly lifetimeGrossRevenue: number;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

export interface ListPlatformTenantsOptions {
  readonly page?: number;
  readonly pageSize?: number;
  readonly status?: string;
  readonly search?: string;
}

export const listPlatformTenants = (
  options: ListPlatformTenantsOptions = {},
): Promise<PlatformTenantListResponse> =>
  apiFetch('/api/v1/admin/platform/tenants', {
    query: {
      page: options.page,
      pageSize: options.pageSize,
      status: options.status,
      search: options.search,
    },
  });

export const getPlatformTenant = (tenantId: string): Promise<PlatformTenant> =>
  apiFetch(`/api/v1/admin/platform/tenants/${encodeURIComponent(tenantId)}`);

export const suspendTenant = (tenantId: string, reason: string): Promise<void> =>
  apiFetch(
    `/api/v1/admin/platform/tenants/${encodeURIComponent(tenantId)}/suspend`,
    { method: 'POST', body: { reason } },
  );

export const reactivateTenant = (tenantId: string): Promise<void> =>
  apiFetch(
    `/api/v1/admin/platform/tenants/${encodeURIComponent(tenantId)}/reactivate`,
    { method: 'POST' },
  );

export const setPlatformFee = (
  tenantId: string,
  bps: number,
): Promise<void> =>
  apiFetch(
    `/api/v1/admin/platform/tenants/${encodeURIComponent(tenantId)}/platform-fee`,
    { method: 'PUT', body: { bps } },
  );
