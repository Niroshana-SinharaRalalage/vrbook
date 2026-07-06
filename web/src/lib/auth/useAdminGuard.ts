'use client';

import { useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { useAuthedQuery } from '../../hooks/useAuthedQuery';
import { getCurrentUser, type CurrentUser } from '../api/me';
import { getMyTenants } from '../tenants/useMyTenants';
import type { MyTenantsResponse } from '../tenants/useMyTenants';
import { isSocialIdp } from './identityProvider';

export type AdminGuardStatus =
  | 'loading'
  | 'unauthenticated'
  | 'ok'
  | 'social-admin-rejected';

export interface AdminGuardResult {
  readonly status: AdminGuardStatus;
  /** Non-null when status='social-admin-rejected'; identifies which provider so the error page can name it. */
  readonly identityProvider?: string;
}

/**
 * Slice OPS.M.12.7 — client-side companion to the backend
 * AdminSocialIdpRejectionMiddleware. Reads the id token's `idp` claim + fetches
 * `/api/v1/me` (isPlatformAdmin) + `/api/v1/me/tenants` (any active membership)
 * — both paths are on the middleware whitelist so they always succeed.
 *
 * Predicate matches the backend Layer 2 gate:
 *   idp ∈ SocialIdpValues AND (isPlatformAdmin OR memberships.length > 0)
 *
 * Owner policy locked 2026-07-05: admins may NEVER hold a social identity, so
 * if the guard fires the SPA redirects to `/auth/admin-social-idp-rejected`
 * with a "Sign out and try again" CTA.
 *
 * Placement: inside `admin/layout.tsx` (via <AdminAuthGuard>) and
 * `select-tenant/page.tsx`. NOT global — see §6.3 in the plan.
 */
export const useAdminGuard = (): AdminGuardResult => {
  const { instance, accounts, inProgress } = useMsal();
  const account = accounts[0] ?? null;

  const meQuery = useAuthedQuery<CurrentUser>({
    queryKey: ['admin-guard', 'me'],
    queryFn: getCurrentUser,
    enabled: Boolean(account) && inProgress === InteractionStatus.None,
    staleTime: 60_000,
  });

  const tenantsQuery = useAuthedQuery<MyTenantsResponse>({
    queryKey: ['admin-guard', 'me-tenants'],
    queryFn: getMyTenants,
    enabled: Boolean(account) && inProgress === InteractionStatus.None,
    staleTime: 60_000,
  });

  if (inProgress !== InteractionStatus.None) {
    return { status: 'loading' };
  }
  if (!account) {
    return { status: 'unauthenticated' };
  }
  if (meQuery.isPending || tenantsQuery.isPending) {
    return { status: 'loading' };
  }
  if (meQuery.isError || tenantsQuery.isError || !meQuery.data || !tenantsQuery.data) {
    // Fail-open: we couldn't fetch the whitelisted /me endpoints, so we don't
    // have evidence of the reject condition. Let downstream API calls surface
    // the error via the standard `admin/error.tsx` branch.
    return { status: 'ok' };
  }

  const claims = (account.idTokenClaims ?? {}) as Record<string, unknown>;
  const idp = typeof claims.idp === 'string' ? claims.idp : null;
  const hasAdminAuthority =
    meQuery.data.isPlatformAdmin ||
    (tenantsQuery.data.memberships?.length ?? 0) > 0;

  if (idp && isSocialIdp(idp) && hasAdminAuthority) {
    // Best-effort: nudge MSAL to discard the cached account so a re-signin
    // fetches a fresh token. logoutRedirect is not called here — the error
    // page's CTA handles the actual sign-out (the user should confirm).
    void instance;
    return { status: 'social-admin-rejected', identityProvider: idp };
  }

  return { status: 'ok' };
};
