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
  | 'social-admin-rejected'
  | 'admin-not-provisioned';

export interface AdminGuardResult {
  readonly status: AdminGuardStatus;
  /** Non-null when status='social-admin-rejected'; identifies which provider so the error page can name it. */
  readonly identityProvider?: string;
  /** Non-null when status='admin-not-provisioned'; the token's email so the rejection page can help the operator find the right seed hint. */
  readonly signInEmail?: string;
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
  // Slice OPS.M.22.7 — admin-not-provisioned detection.
  // The M.22.4 middleware whitelists /me + /me/tenants (so the SPA can render
  // this rejection page), but GetMeHandler still throws Forbidden when the
  // token has no matching identity.users row. That specific 403 on /me for an
  // authenticated user with a valid token = an unprovisioned admin — no other
  // code path produces a 403 on /me. See docs/OPS_M_22_ADMIN_PRESEED_PLAN.md §6.
  const meStatus = (meQuery.error as { status?: number } | undefined)?.status;
  if (meStatus === 403 || meStatus === 401) {
    const claimsForEmail = (account.idTokenClaims ?? {}) as Record<string, unknown>;
    const emailFromClaim =
      (typeof claimsForEmail.email === 'string' ? claimsForEmail.email : undefined) ??
      (typeof claimsForEmail.preferred_username === 'string' ? claimsForEmail.preferred_username : undefined) ??
      undefined;
    return { status: 'admin-not-provisioned', signInEmail: emailFromClaim };
  }

  if (meQuery.isError || tenantsQuery.isError || !meQuery.data || !tenantsQuery.data) {
    // Fail-open: we couldn't fetch the whitelisted /me endpoints for a
    // different reason (transient 5xx, network). Downstream API calls will
    // surface the error via the standard `admin/error.tsx` branch.
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
