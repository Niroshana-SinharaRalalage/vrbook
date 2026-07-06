'use client';

/**
 * Slice OPS.M.13.5 — tenant picker page. Shown post-sign-in when the caller
 * has more than one active tenant membership. Selecting a tenant writes the
 * id to sessionStorage (M.13.5) and routes back to the deep-link target;
 * M.13.6 hooks that sessionStorage value into the api client's X-Active-Tenant
 * header on every subsequent request.
 */

import { Suspense, useMemo } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMyTenants, MyTenantMembership } from '../../lib/tenants/useMyTenants';
import { setActiveTenantId } from '../../lib/tenants/activeTenant';
import { AdminAuthGuard } from '../../components/auth/AdminAuthGuard';

const isSelectable = (status: MyTenantMembership['status']): boolean =>
  status === 'Active' || status === 'PendingOnboarding';

const statusLabel = (status: MyTenantMembership['status']): string => {
  switch (status) {
    case 'Active':
      return 'Active';
    case 'PendingOnboarding':
      return 'Setup in progress';
    case 'Suspended':
      return 'Suspended';
    case 'Closed':
      return 'Closed';
    default:
      return status;
  }
};

const SelectTenantInner = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const returnTo = searchParams.get('returnTo') ?? '/';
  const { data, isLoading, isError, error } = useMyTenants();

  const orderedMemberships = useMemo(() => {
    if (!data?.memberships) return [];
    return [...data.memberships].sort((a, b) => {
      if (a.isPrimary !== b.isPrimary) return a.isPrimary ? -1 : 1;
      return a.displayName.localeCompare(b.displayName);
    });
  }, [data]);

  const select = (tenantId: string): void => {
    setActiveTenantId(tenantId);
    router.replace(returnTo);
  };

  if (isLoading) {
    return (
      <main className="flex min-h-dvh items-center justify-center">
        <div className="text-sm text-muted-foreground">Loading your tenants…</div>
      </main>
    );
  }

  if (isError) {
    return (
      <main className="flex min-h-dvh items-center justify-center p-6">
        <div className="max-w-md text-center">
          <h1 className="text-xl font-semibold">Could not load tenants</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            {error instanceof Error ? error.message : 'Unknown error.'}
          </p>
        </div>
      </main>
    );
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col items-center justify-center gap-6 p-6">
      <div className="text-center">
        <h1 className="text-2xl font-semibold">Choose a tenant</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          You have access to more than one tenant. Pick one to continue.
        </p>
      </div>

      <ul className="w-full space-y-2">
        {orderedMemberships.map((m) => {
          const selectable = isSelectable(m.status);
          return (
            <li key={m.tenantId}>
              <button
                type="button"
                onClick={() => selectable && select(m.tenantId)}
                disabled={!selectable}
                className="flex w-full items-center justify-between rounded-md border p-4 text-left transition hover:bg-muted/40 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <div>
                  <div className="font-medium">
                    {m.displayName}
                    {m.isPrimary && (
                      <span className="ml-2 rounded bg-primary/10 px-2 py-0.5 text-xs font-normal text-primary">
                        primary
                      </span>
                    )}
                  </div>
                  <div className="text-xs text-muted-foreground">
                    {m.slug} · {m.role} · {statusLabel(m.status)}
                  </div>
                </div>
                {selectable && <span className="text-sm text-primary">Enter →</span>}
              </button>
            </li>
          );
        })}
      </ul>

      {data?.isPlatformAdmin && (
        <div className="w-full rounded-md border border-dashed p-4 text-center text-sm">
          <p className="mb-2 text-muted-foreground">
            You are a Platform Admin. Skip the tenant picker and go to the platform dashboard.
          </p>
          <button
            type="button"
            onClick={() => router.replace('/admin/platform')}
            className="text-sm font-medium text-primary hover:underline"
          >
            Open Platform Dashboard →
          </button>
        </div>
      )}

      <div className="text-xs text-muted-foreground">
        Not the right account?{' '}
        <a href="/auth/signout" className="font-medium underline">
          Sign in as a different user
        </a>
      </div>
    </main>
  );
};

// Slice OPS.M.12.7 — the tenant picker is admin-authority territory. Guard
// with <AdminAuthGuard> so a social-IdP token attempting the admin flow is
// redirected to `/auth/admin-social-idp-rejected` BEFORE the API rejects.
const SelectTenantPage = () => (
  <AdminAuthGuard>
    <Suspense
      fallback={
        <main className="flex min-h-dvh items-center justify-center">
          <div className="text-sm text-muted-foreground">Loading…</div>
        </main>
      }
    >
      <SelectTenantInner />
    </Suspense>
  </AdminAuthGuard>
);

export default SelectTenantPage;
