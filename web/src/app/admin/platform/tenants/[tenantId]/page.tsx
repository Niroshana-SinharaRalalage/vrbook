'use client';

/**
 * Slice OPS.M.8 §3.7 + §3.9 Step 10 — platform-admin tenant detail.
 *
 * Surfaces every operator-relevant field plus three actions: Suspend
 * (POST + reason), Reactivate (POST), Set platform fee (PUT). All three
 * round-trip through [Authorize(Roles='PlatformAdmin')] endpoints; the
 * web layer just renders.
 */
import { useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getPlatformTenant,
  suspendTenant,
  reactivateTenant,
  setPlatformFee,
} from '@/lib/api/platform';
import { ApiProblemError } from '@/lib/api/client';
import { cn } from '@/lib/utils/cn';

const PlatformTenantDetailPage = () => {
  const params = useParams();
  const router = useRouter();
  const tenantId = String(params.tenantId);
  const qc = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['platform', 'tenant', tenantId],
    queryFn: () => getPlatformTenant(tenantId),
  });

  const [suspendReason, setSuspendReason] = useState('');
  const [showSuspendModal, setShowSuspendModal] = useState(false);
  const [feeInput, setFeeInput] = useState<string>('');
  const [actionError, setActionError] = useState<string | null>(null);

  const invalidate = () =>
    qc.invalidateQueries({ queryKey: ['platform', 'tenant', tenantId] });

  const suspendMu = useMutation({
    mutationFn: (reason: string) => suspendTenant(tenantId, reason),
    onSuccess: () => {
      setShowSuspendModal(false);
      setSuspendReason('');
      void invalidate();
    },
    onError: (e) => setActionError(e instanceof Error ? e.message : 'Suspend failed.'),
  });

  const reactivateMu = useMutation({
    mutationFn: () => reactivateTenant(tenantId),
    onSuccess: () => {
      void invalidate();
    },
    onError: (e) => setActionError(e instanceof Error ? e.message : 'Reactivate failed.'),
  });

  const feeMu = useMutation({
    mutationFn: (bps: number) => setPlatformFee(tenantId, bps),
    onSuccess: () => {
      setFeeInput('');
      void invalidate();
    },
    onError: (e) => setActionError(e instanceof Error ? e.message : 'Set fee failed.'),
  });

  if (isLoading) {
    return <p className="py-8 text-center text-muted-foreground">Loading tenant…</p>;
  }
  if (isError) {
    const status = error instanceof ApiProblemError ? error.status : 0;
    if (status === 404) {
      return (
        <div className="py-8">
          <h1 className="text-xl font-semibold">Tenant not found</h1>
          <button
            type="button"
            onClick={() => router.push('/admin/platform/tenants')}
            className="mt-3 text-sm underline"
          >
            Back to list
          </button>
        </div>
      );
    }
    return (
      <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-4 text-sm">
        {error instanceof Error ? error.message : 'Failed to load tenant.'}
      </div>
    );
  }
  if (!data) return null;

  return (
    <div className="space-y-6 py-6">
      <header className="flex items-baseline justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{data.displayName}</h1>
          <p className="text-sm text-muted-foreground">{data.slug}</p>
        </div>
        <StatusBadge status={data.status} />
      </header>

      {data.status === 'Suspended' && data.suspendedReason && (
        <div role="status" className="rounded-md border border-amber-300 bg-amber-50/40 p-4 text-sm text-amber-900 dark:bg-amber-950/30 dark:text-amber-200">
          <strong>Suspended:</strong> {data.suspendedReason}
        </div>
      )}

      <section className="grid grid-cols-2 gap-4 rounded-lg border border-border p-4 text-sm">
        <Field label="Status" value={data.status} />
        <Field label="Default currency" value={data.defaultCurrency} />
        <Field label="Platform fee (bps)" value={String(data.platformFeeBps)} />
        <Field
          label="Stripe"
          value={
            data.hasStripeAccount
              ? `${data.stripeAccountStatus ?? 'Active'} · charges ${data.chargesEnabled ? 'on' : 'off'}, payouts ${data.payoutsEnabled ? 'on' : 'off'}`
              : 'No account'
          }
        />
        <Field label="Properties" value={String(data.propertyCount)} />
        <Field label="Active bookings" value={String(data.activeBookingCount)} />
        <Field label="Total bookings" value={String(data.totalBookingCount)} />
        <Field
          label="Lifetime revenue"
          value={`${data.lifetimeGrossRevenue.toFixed(2)} ${data.defaultCurrency}`}
        />
        <Field
          label="Created"
          value={new Date(data.createdAt).toISOString().slice(0, 10)}
        />
        <Field
          label="Updated"
          value={data.updatedAt ? new Date(data.updatedAt).toISOString().slice(0, 10) : '—'}
        />
      </section>

      {actionError && (
        <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          {actionError}
        </div>
      )}

      <section className="space-y-3 rounded-lg border border-border p-4">
        <h2 className="text-lg font-semibold">Actions</h2>
        <div className="flex flex-wrap gap-3">
          {data.status === 'Active' && (
            <button
              type="button"
              onClick={() => {
                setActionError(null);
                setShowSuspendModal(true);
              }}
              className="rounded-md bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700"
            >
              Suspend tenant
            </button>
          )}
          {data.status === 'Suspended' && (
            <button
              type="button"
              onClick={() => {
                setActionError(null);
                reactivateMu.mutate();
              }}
              disabled={reactivateMu.isPending}
              className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60"
            >
              {reactivateMu.isPending ? 'Reactivating…' : 'Reactivate tenant'}
            </button>
          )}
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            setActionError(null);
            const bps = parseInt(feeInput, 10);
            if (!Number.isInteger(bps) || bps < 0 || bps > 10000) {
              setActionError('Fee bps must be an integer 0–10000.');
              return;
            }
            feeMu.mutate(bps);
          }}
          className="flex items-center gap-2"
        >
          <label className="text-sm" htmlFor="fee-bps">Platform fee (bps)</label>
          <input
            id="fee-bps"
            type="number"
            inputMode="numeric"
            min={0}
            max={10000}
            value={feeInput}
            placeholder={String(data.platformFeeBps)}
            onChange={(e) => setFeeInput(e.target.value)}
            className="w-28 rounded-md border border-border bg-background px-2 py-1 text-sm"
          />
          <button
            type="submit"
            disabled={feeMu.isPending}
            className="rounded-md bg-brand-maroon-600 px-3 py-1 text-sm text-white hover:bg-brand-maroon-700 disabled:opacity-60"
          >
            {feeMu.isPending ? 'Saving…' : 'Set fee'}
          </button>
        </form>
      </section>

      {showSuspendModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="suspend-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
        >
          <div className="w-full max-w-md rounded-lg bg-card p-5 shadow-lg">
            <h2 id="suspend-dialog-title" className="text-lg font-semibold">
              Suspend {data.displayName}?
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              The tenant will move from Active to Suspended. A reason is required
              for the audit log.
            </p>
            <textarea
              aria-label="Suspension reason"
              value={suspendReason}
              onChange={(e) => setSuspendReason(e.target.value)}
              rows={3}
              className="mt-3 w-full rounded-md border border-border bg-background p-2 text-sm"
            />
            <div className="mt-4 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowSuspendModal(false)}
                className="rounded-md border border-border px-3 py-1 text-sm"
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={!suspendReason.trim() || suspendMu.isPending}
                onClick={() => suspendMu.mutate(suspendReason.trim())}
                className="rounded-md bg-amber-600 px-3 py-1 text-sm text-white hover:bg-amber-700 disabled:opacity-60"
              >
                {suspendMu.isPending ? 'Suspending…' : 'Suspend'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

const Field = ({ label, value }: { readonly label: string; readonly value: string }) => (
  <div>
    <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
    <div className="font-medium">{value}</div>
  </div>
);

const StatusBadge = ({ status }: { readonly status: string }) => (
  <span
    className={cn(
      'inline-block rounded-full px-3 py-1 text-sm',
      status === 'Active' && 'bg-emerald-100 text-emerald-900 dark:bg-emerald-900/40 dark:text-emerald-200',
      status === 'Suspended' && 'bg-amber-100 text-amber-900 dark:bg-amber-900/40 dark:text-amber-200',
      status === 'PendingOnboarding' && 'bg-sky-100 text-sky-900 dark:bg-sky-900/40 dark:text-sky-200',
      status === 'Closed' && 'bg-muted text-muted-foreground',
    )}
  >
    {status}
  </span>
);

export default PlatformTenantDetailPage;
