'use client';

import { useEffect, type ReactNode } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth/useAuth';

/**
 * Slice OPS.M.12.6 — client-side guard that gates the admin subtree. If the
 * user is not authenticated, redirect to `/auth/signin?flow=admin&returnTo=<pathname>`
 * so the sign-in kick-off routes through the admin Entra user flow (no social
 * buttons).
 *
 * The M.12.7 slice adds an admin-social-idp-rejected branch to this guard —
 * this component only handles the unauthenticated case for now.
 *
 * Placement: only inside `web/src/app/admin/layout.tsx` (and later
 * `select-tenant/page.tsx`), NOT in `Providers.tsx`. Placing it globally
 * would force EVERY route to require auth, breaking the public marketing
 * surface. (§6.3 in the plan.)
 */
export const AdminAuthGuard = ({ children }: { readonly children: ReactNode }) => {
  const { isAuthenticated, isBusy } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (isBusy) return;
    if (isAuthenticated) return;
    const returnTo = pathname ?? '/admin';
    router.replace(`/auth/signin?flow=admin&returnTo=${encodeURIComponent(returnTo)}`);
  }, [isAuthenticated, isBusy, pathname, router]);

  if (!isAuthenticated) {
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
