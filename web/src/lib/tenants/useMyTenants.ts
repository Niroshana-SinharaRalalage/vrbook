/**
 * Slice OPS.M.13.5 — react-query hook wrapping GET /api/v1/me/tenants.
 * Drives the /select-tenant page + the post-sign-in routing in the auth callback.
 */
'use client';

import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '../api/client';

export interface MyTenantMembership {
  readonly tenantId: string;
  readonly slug: string;
  readonly displayName: string;
  readonly status: 'PendingOnboarding' | 'Active' | 'Suspended' | 'Closed';
  readonly role: string;
  readonly isPrimary: boolean;
}

export interface MyTenantsResponse {
  readonly memberships: readonly MyTenantMembership[];
  readonly isPlatformAdmin: boolean;
}

export const getMyTenants = (): Promise<MyTenantsResponse> =>
  apiFetch<MyTenantsResponse>('/api/v1/me/tenants');

export const useMyTenants = () =>
  useQuery({
    queryKey: ['me', 'tenants'],
    queryFn: getMyTenants,
    staleTime: 30_000,
  });
