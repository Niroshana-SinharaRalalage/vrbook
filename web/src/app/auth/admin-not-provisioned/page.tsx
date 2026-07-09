'use client';

import { Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../../lib/auth/useAuth';

/**
 * Slice OPS.M.22.7 — surfaces the admin-preseed rejection (owner
 * policy locked 2026-07-07). Complements the M.12.7
 * <c>/auth/admin-social-idp-rejected</c> page: this one fires when the
 * caller is signed in through the admin flow but no operator has
 * pre-seeded an <c>identity.users</c> row for their email.
 *
 * <p>Two entry points:</p>
 * <ul>
 *   <li><c>AdminAuthGuard</c> client-redirects here when
 *   <c>useAdminGuard()</c> returns <c>admin-not-provisioned</c> (any 401/403
 *   from <c>/api/v1/me</c> on an otherwise-authenticated session).</li>
 *   <li><c>admin/error.tsx</c> bubbles a 401 with
 *   <c>type=admin-account-not-provisioned</c> to this route (via a link).</li>
 * </ul>
 *
 * <p>Optional query param <c>?email=&lt;value&gt;</c> lets the page display
 * the exact email the operator needs to seed. When missing, generic copy.</p>
 */
const AdminNotProvisionedInner = () => {
  const params = useSearchParams();
  const { signOut } = useAuth();
  const email = params.get('email') ?? undefined;

  return (
    <main className="mx-auto flex min-h-[70vh] max-w-lg flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="text-2xl font-semibold">Your account hasn&apos;t been provisioned yet</h1>
      <p className="text-sm text-muted-foreground">
        Sign-in succeeded, but no admin record exists for this email. Contact
        your workspace operator with the exact email address you signed in
        with so they can seed your account, then sign in again.
      </p>
      {email ? (
        <p className="text-sm">
          <span className="text-muted-foreground">Signed in as:</span>{' '}
          <span className="font-mono font-medium">{email}</span>
        </p>
      ) : null}
      <p className="text-xs text-muted-foreground">
        Operators: run{' '}
        <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-[11px]">
          .\vrbook-admin.ps1 -Env &lt;env&gt; -Action seed-platform-admin -Email &lt;email&gt; -DisplayName &lt;name&gt;
        </span>{' '}
        or add the email to <span className="font-mono text-[11px]">seedPlatformAdmins</span> in{' '}
        <span className="font-mono text-[11px]">infra/main.bicep</span> and redeploy.
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

const AdminNotProvisionedPage = () => (
  <Suspense
    fallback={
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-sm text-muted-foreground">Loading…</div>
      </main>
    }
  >
    <AdminNotProvisionedInner />
  </Suspense>
);

export default AdminNotProvisionedPage;
