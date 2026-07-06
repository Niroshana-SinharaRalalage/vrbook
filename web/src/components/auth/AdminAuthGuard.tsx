'use client';

import { useEffect, type ReactNode } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth/useAuth';
import { useAdminGuard } from '@/lib/auth/useAdminGuard';

/**
 * Slice OPS.M.12.6/12.7 — client-side guard that gates the admin subtree.
 *
 *   - Unauthenticated → `/auth/signin?flow=admin&returnTo=<pathname>` so the
 *     kick-off routes through the admin Entra user flow (no social buttons).
 *   - Social-IdP token attempting admin authority (owner policy locked
 *     2026-07-05; see ADR-0016) → `/auth/admin-social-idp-rejected?provider=...`
 *     so the SPA shows the rejection copy + "Sign out and try again" CTA
 *     BEFORE any admin API call is made. This mirrors the backend Layer 2
 *     middleware, which fails the same request 403 with problem type
 *     `admin-social-idp-rejected` if the SPA somehow slips past.
 *
 * Placement: only inside `admin/layout.tsx` + `select-tenant/page.tsx`. NOT
 * global — see §6.3 in the plan.
 */
export const AdminAuthGuard = ({ children }: { readonly children: ReactNode }) => {
  const { isAuthenticated, isBusy } = useAuth();
  const guard = useAdminGuard();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (isBusy) return;
    if (!isAuthenticated) {
      const returnTo = pathname ?? '/admin';
      router.replace(`/auth/signin?flow=admin&returnTo=${encodeURIComponent(returnTo)}`);
      return;
    }
    if (guard.status === 'social-admin-rejected') {
      const q = guard.identityProvider
        ? `?provider=${encodeURIComponent(guard.identityProvider)}`
        : '';
      router.replace(`/auth/admin-social-idp-rejected${q}`);
    }
  }, [
    isAuthenticated,
    isBusy,
    pathname,
    router,
    guard.status,
    guard.identityProvider,
  ]);

  if (!isAuthenticated || guard.status === 'social-admin-rejected' || guard.status === 'loading') {
    return (
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-center text-sm text-muted-foreground">
          Checking sign-in…
        </div>
      </main>
    );
  }

  return <>{children}</>;
};
