'use client';

/**
 * Slice OPS.M.13.7 — admin subtree error boundary.
 *
 * <p>Catches the RFC 7807 problems bubbled up from admin API calls. Two
 * cases matter for M.13:</p>
 *
 * <ol>
 *   <li>API returned a 403 with detail beginning "Cross-tenant write
 *       rejected" — the caller signed in against tenant A but the
 *       resource lives in tenant B. If they have &gt;1 active membership,
 *       the fix is to switch workspace (sign out + sign in + pick B on
 *       the picker). Show a Switch workspace CTA.</li>
 *   <li>Everything else — generic "Something went wrong" with a
 *       Retry button.</li>
 * </ol>
 *
 * <p>Uses <see cref="useMyTenants"/> to gate the CTA on membership
 * count: for a single-tenant user, sign-out won't help (there's nowhere
 * else to switch to) so we fall through to the generic message.</p>
 */

import Link from 'next/link';
import { useMyTenants } from '@/lib/tenants/useMyTenants';
import { ApiProblemError } from '@/lib/api/client';

interface AdminErrorProps {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}

const isCrossTenantWriteRejected = (error: Error): boolean => {
  if (!(error instanceof ApiProblemError)) return false;
  if (error.status !== 403) return false;
  const detail = error.problem.detail ?? error.problem.title ?? '';
  return detail.startsWith('Cross-tenant write rejected');
};

// Slice OPS.M.12.7 — the middleware admin-vs-social gate uses this problem
// type. See src/VrBook.Contracts/Common/ProblemTypes.cs.
const ADMIN_SOCIAL_IDP_REJECTED_TYPE = 'https://vrbook.example.com/problems/admin-social-idp-rejected';

const isAdminSocialIdpRejected = (error: Error): { rejected: true; identityProvider?: string } | { rejected: false } => {
  if (!(error instanceof ApiProblemError)) return { rejected: false };
  if (error.status !== 403) return { rejected: false };
  if (error.problem.type !== ADMIN_SOCIAL_IDP_REJECTED_TYPE) return { rejected: false };
  const idp = error.problem.identityProvider;
  return { rejected: true, identityProvider: typeof idp === 'string' ? idp : undefined };
};

const AdminError = ({ error, reset }: AdminErrorProps) => {
  const { data } = useMyTenants();
  const membershipCount = data?.memberships.length ?? 0;
  const isCrossTenant = isCrossTenantWriteRejected(error);
  const canSwitchWorkspace = isCrossTenant && membershipCount > 1;
  const socialAdmin = isAdminSocialIdpRejected(error);

  if (socialAdmin.rejected) {
    const q = socialAdmin.identityProvider
      ? `?provider=${encodeURIComponent(socialAdmin.identityProvider)}`
      : '';
    return (
      <main className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-4 p-6 text-center">
        <h1 className="text-2xl font-semibold">Admin sign-in requires a workspace account</h1>
        <p className="text-sm text-muted-foreground">
          Social sign-in is available for guest use only. Admin authority
          requires an Entra workspace account (email + password or OTP).
        </p>
        <Link
          href={`/auth/admin-social-idp-rejected${q}`}
          className="mt-4 inline-flex h-10 items-center rounded-md bg-brand-orange-600 px-4 text-sm font-medium text-white hover:bg-brand-orange-700"
        >
          Sign out and try again
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center gap-4 p-6 text-center">
      {canSwitchWorkspace ? (
        <>
          <h1 className="text-2xl font-semibold">Wrong workspace</h1>
          <p className="text-sm text-muted-foreground">
            You need to switch workspace to view this. Sign out and pick a
            different workspace when you sign in again.
          </p>
          <div className="mt-4 flex flex-col items-center gap-2">
            <Link
              href="/auth/signout"
              className="inline-flex h-10 items-center rounded-md bg-brand-orange-600 px-4 text-sm font-medium text-white hover:bg-brand-orange-700"
            >
              Sign out
            </Link>
            <button
              type="button"
              onClick={reset}
              className="text-xs text-muted-foreground underline hover:text-foreground"
            >
              Retry (if you think you&apos;re already in the right workspace)
            </button>
          </div>
        </>
      ) : isCrossTenant ? (
        <>
          <h1 className="text-2xl font-semibold">Access denied</h1>
          <p className="text-sm text-muted-foreground">
            You don&apos;t have permission to view this. If you believe this is
            a mistake, contact the workspace owner.
          </p>
          <Link
            href="/admin"
            className="mt-4 inline-flex h-10 items-center rounded-md border px-4 text-sm font-medium hover:bg-muted"
          >
            Back to dashboard
          </Link>
        </>
      ) : (
        <>
          <h1 className="text-2xl font-semibold">Something went wrong</h1>
          <p className="text-sm text-muted-foreground">
            {error.message || 'The admin page hit an unexpected error.'}
          </p>
          <button
            type="button"
            onClick={reset}
            className="mt-4 inline-flex h-10 items-center rounded-md bg-brand-orange-600 px-4 text-sm font-medium text-white hover:bg-brand-orange-700"
          >
            Retry
          </button>
        </>
      )}
    </main>
  );
};

export default AdminError;
