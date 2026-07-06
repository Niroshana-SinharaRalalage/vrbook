'use client';

import { Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../../lib/auth/useAuth';

/**
 * Slice OPS.M.12.7 — surfaces the admin-vs-social-IdP rejection
 * (owner policy locked 2026-07-05; see ADR-0016).
 *
 * Two entry points:
 *   1. `<AdminAuthGuard>` client-redirects here when `useAdminGuard()`
 *      returns `social-admin-rejected`.
 *   2. `admin/error.tsx` bubbles a 403 with `type=admin-social-idp-rejected`
 *      to this page's route (via a Link).
 *
 * Optional query parameter `?provider=google.com` lets the page name the
 * specific provider ("You signed in with Google…"); missing = generic copy.
 */
const AdminSocialIdpRejectedInner = () => {
  const params = useSearchParams();
  const { signOut } = useAuth();
  const provider = params.get('provider') ?? undefined;

  const providerLabel = (() => {
    if (!provider) return null;
    const lower = provider.toLowerCase();
    if (lower.includes('google')) return 'Google';
    if (lower.includes('live') || lower === 'microsoft') return 'Microsoft consumer';
    if (lower.includes('facebook')) return 'Facebook';
    if (lower.includes('apple')) return 'Apple';
    return null;
  })();

  return (
    <main className="mx-auto flex min-h-[70vh] max-w-lg flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="text-2xl font-semibold">Admin sign-in requires a workspace account</h1>
      <p className="text-sm text-muted-foreground">
        {providerLabel
          ? `You signed in with ${providerLabel}. Social sign-in is available for guest use only — admin authority requires an Entra workspace account (email + password or OTP).`
          : 'Social sign-in is available for guest use only — admin authority requires an Entra workspace account (email + password or OTP).'}
      </p>
      <p className="text-xs text-muted-foreground">
        This restriction is enforced by owner policy. If you believe this is a
        mistake, contact the workspace owner.
      </p>
      <div className="mt-4 flex flex-col items-center gap-2">
        <button
          type="button"
          onClick={signOut}
          className="inline-flex h-10 items-center rounded-md bg-brand-orange-600 px-4 text-sm font-medium text-white hover:bg-brand-orange-700"
        >
          Sign out and try again
        </button>
        <Link href="/" className="text-xs text-muted-foreground underline hover:text-foreground">
          Go to the homepage instead
        </Link>
      </div>
    </main>
  );
};

const AdminSocialIdpRejectedPage = () => (
  <Suspense
    fallback={
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-sm text-muted-foreground">Loading…</div>
      </main>
    }
  >
    <AdminSocialIdpRejectedInner />
  </Suspense>
);

export default AdminSocialIdpRejectedPage;
