'use client';

/**
 * Slice OPS.M.8 §3.7 (D7) Step 10 — platform-admin tenant list. The
 * server-side [Authorize(Roles='PlatformAdmin')] gate guarantees a 403
 * for non-admins; the web layer just renders.
 */
import { useState } from 'react';
import Link from 'next/link';
import { listPlatformTenants, type PlatformTenantListResponse } from '@/lib/api/platform';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import { cn } from '@/lib/utils/cn';

const STATUSES = [
  { value: '', label: 'All statuses' },
  { value: 'PendingOnboarding', label: 'Pending onboarding' },
  { value: 'Active', label: 'Active' },
  { value: 'Suspended', label: 'Suspended' },
  { value: 'Closed', label: 'Closed' },
];

const PlatformTenantsPage = () => {
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const { data, isLoading, isError, error, needsSignIn } = useAuthedQuery<PlatformTenantListResponse>({
    queryKey: ['platform', 'tenants', { page, status, search }],
    queryFn: () =>
      listPlatformTenants({ page, pageSize: 25, status: status || undefined, search: search || undefined }),
  });

  if (needsSignIn) {
    return <SignInGate title="Sign in to view platform tenants" />;
  }

  return (
    <div className="space-y-4 py-6">
      <header>
        <h1 className="text-2xl font-semibold">Platform — Tenants</h1>
        <p className="text-sm text-muted-foreground">
          Every tenant on the platform. Operator-only surface.
        </p>
      </header>

      <div className="flex flex-wrap gap-3">
        <select
          aria-label="Filter by status"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
          className="rounded-md border border-border bg-background px-3 py-2 text-sm"
        >
          {STATUSES.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
        <input
          aria-label="Search tenants by slug or name"
          type="search"
          placeholder="Search slug or name…"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          className="flex-1 rounded-md border border-border bg-background px-3 py-2 text-sm"
        />
      </div>

      {isLoading && (
        <p className="py-8 text-center text-muted-foreground">Loading tenants…</p>
      )}

      {isError && (
        <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-4 text-sm">
          {error instanceof Error ? error.message : 'Failed to load tenants.'}
        </div>
      )}

      {data && (
        <>
          <table className="w-full text-sm">
            <thead className="border-b border-border text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="py-2 pr-3">Tenant</th>
                <th className="py-2 pr-3">Status</th>
                <th className="py-2 pr-3">Stripe</th>
                <th className="py-2 pr-3">Fee (bps)</th>
                <th className="py-2 pr-3">Currency</th>
                <th className="py-2">Created</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((t) => (
                <tr key={t.id} className="border-b border-border last:border-0 hover:bg-accent/30">
                  <td className="py-2 pr-3">
                    <Link
                      href={`/admin/platform/tenants/${t.id}`}
                      className="font-medium text-foreground hover:underline"
                    >
                      {t.displayName}
                    </Link>
                    <span className="ml-2 text-xs text-muted-foreground">{t.slug}</span>
                  </td>
                  <td className="py-2 pr-3">
                    <StatusBadge status={t.status} />
                  </td>
                  <td className="py-2 pr-3">
                    {t.hasStripeAccount
                      ? t.chargesEnabled && t.payoutsEnabled
                        ? 'Active'
                        : 'Pending'
                      : 'None'}
                  </td>
                  <td className="py-2 pr-3 tabular-nums">{t.platformFeeBps}</td>
                  <td className="py-2 pr-3">{t.defaultCurrency}</td>
                  <td className="py-2 text-muted-foreground">
                    {new Date(t.createdAt).toISOString().slice(0, 10)}
                  </td>
                </tr>
              ))}
              {data.items.length === 0 && (
                <tr>
                  <td colSpan={6} className="py-6 text-center text-muted-foreground">
                    No tenants match.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div className="flex items-center justify-between text-sm text-muted-foreground">
            <span>
              Page {data.page} of {Math.max(1, Math.ceil(data.total / data.pageSize))} ·
              {' '}{data.total} total
            </span>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage((n) => Math.max(1, n - 1))}
                className="rounded-md border border-border px-3 py-1 disabled:opacity-50"
              >
                Previous
              </button>
              <button
                type="button"
                disabled={page * data.pageSize >= data.total}
                onClick={() => setPage((n) => n + 1)}
                className="rounded-md border border-border px-3 py-1 disabled:opacity-50"
              >
                Next
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
};

const StatusBadge = ({ status }: { readonly status: string }) => (
  <span
    className={cn(
      'inline-block rounded-full px-2 py-0.5 text-xs',
      status === 'Active' && 'bg-emerald-100 text-emerald-900 dark:bg-emerald-900/40 dark:text-emerald-200',
      status === 'Suspended' && 'bg-amber-100 text-amber-900 dark:bg-amber-900/40 dark:text-amber-200',
      status === 'PendingOnboarding' && 'bg-sky-100 text-sky-900 dark:bg-sky-900/40 dark:text-sky-200',
      status === 'Closed' && 'bg-muted text-muted-foreground',
    )}
  >
    {status}
  </span>
);

export default PlatformTenantsPage;
