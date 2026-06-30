'use client';

import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, MessageSquare, RefreshCw, Star, X } from 'lucide-react';
import {
  adminHideReview,
  adminListReviews,
  adminRejectReview,
  adminRestoreReview,
  respondToReview,
  type Review,
} from '@/lib/api/reviews';
import { ApiProblemError } from '@/lib/api/client';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';

type StatusFilter = 'All' | Review['status'];

const STATUS_FILTERS: { value: StatusFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Hidden', label: 'Hidden' },
  { value: 'Rejected', label: 'Rejected' },
];

const STATUS_STYLES: Record<Review['status'], string> = {
  Pending: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
  Approved: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
  Hidden: 'bg-slate-200 text-slate-800 dark:bg-slate-800 dark:text-slate-100',
  Rejected: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200',
};

const StarRow = ({ rating }: { rating: number }) => (
  <span className="inline-flex items-center gap-0.5" aria-label={`${rating} stars`}>
    {[1, 2, 3, 4, 5].map((n) => (
      <Star
        key={n}
        className={`h-3.5 w-3.5 ${n <= rating ? 'fill-yellow-400 text-yellow-400' : 'text-muted-foreground/40'}`}
      />
    ))}
  </span>
);

const AdminReviewsPage = () => {
  const [filter, setFilter] = useState<StatusFilter>('All');
  const qc = useQueryClient();
  const QK = ['admin', 'reviews', filter] as const;

  // Slice OPS.M.10.2 F11.7.4.7b — list on useAuthedQuery; mutations
  // invalidate.
  const { data, isLoading, isError, error: queryError, needsSignIn } = useAuthedQuery<readonly Review[]>({
    queryKey: [...QK],
    queryFn: () => adminListReviews(filter === 'All' ? undefined : filter),
  });
  const rows = data ?? [];
  const loading = isLoading;
  const [error, setError] = useState<string | null>(null);
  const queryErrorMsg = isError ? extractErr(queryError, 'Failed to load reviews.') : null;
  const displayError = error ?? queryErrorMsg;
  const reload = async () => {
    setError(null);
    await qc.invalidateQueries({ queryKey: [...QK] });
  };
  const [respondingTo, setRespondingTo] = useState<Review | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const counts = useMemo(() => {
    const totals: Partial<Record<Review['status'], number>> = {};
    for (const r of rows) {
      totals[r.status] = (totals[r.status] ?? 0) + 1;
    }
    return totals;
  }, [rows]);

  const runAction = async (
    id: string,
    action: (id: string) => Promise<void>,
    busyLabel: string,
  ) => {
    setBusyId(id);
    setError(null);
    try {
      await action(id);
      await reload();
    } catch (err) {
      setError(extractErr(err, `${busyLabel} failed.`));
    } finally {
      setBusyId(null);
    }
  };

  if (needsSignIn) {
    return <SignInGate title="Sign in to manage reviews" />;
  }

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Reviews</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Guest reviews for every property you own. Hide takes a review off the public listing
            without deleting it; Reject is a soft delete for spam. Owner responses are one-shot —
            once posted, the box disables.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void reload()}
          className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-2 text-sm hover:bg-accent"
        >
          <RefreshCw className="h-4 w-4" />
          Reload
        </button>
      </header>

      {displayError && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {displayError}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {STATUS_FILTERS.map((f) => {
          const active = filter === f.value;
          return (
            <button
              key={f.value}
              type="button"
              onClick={() => setFilter(f.value)}
              className={`rounded-full border px-3 py-1 text-xs ${
                active
                  ? 'border-brand-maroon-700 bg-brand-maroon-700 text-white'
                  : 'border-border bg-background text-muted-foreground hover:bg-accent'
              }`}
            >
              {f.label}
              {f.value !== 'All' && counts[f.value as Review['status']] != null && (
                <span className="ml-1.5 text-[10px] opacity-80">
                  {counts[f.value as Review['status']]}
                </span>
              )}
            </button>
          );
        })}
      </div>

      <section className="space-y-3">
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">No reviews match the current filter.</p>
        ) : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card">
            {rows.map((r) => (
              <li key={r.id} className="space-y-2 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2 text-sm">
                      <StarRow rating={r.rating} />
                      <span className="font-medium">{r.guestDisplayName}</span>
                      <span className={`rounded-full px-2 py-0.5 text-[11px] ${STATUS_STYLES[r.status]}`}>
                        {r.status}
                      </span>
                    </div>
                    <div className="mt-0.5 text-xs text-muted-foreground">
                      {new Date(r.createdAt).toLocaleString()} · booking{' '}
                      <span className="font-mono">{r.bookingId.slice(0, 8)}</span>
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-1">
                    {r.status === 'Approved' && (
                      <button
                        type="button"
                        disabled={busyId === r.id}
                        onClick={() => void runAction(r.id, adminHideReview, 'Hide')}
                        className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                      >
                        <EyeOff className="h-3 w-3" />
                        Hide
                      </button>
                    )}
                    {r.status === 'Hidden' && (
                      <button
                        type="button"
                        disabled={busyId === r.id}
                        onClick={() => void runAction(r.id, adminRestoreReview, 'Restore')}
                        className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                      >
                        <Eye className="h-3 w-3" />
                        Restore
                      </button>
                    )}
                    {r.status !== 'Rejected' && (
                      <button
                        type="button"
                        disabled={busyId === r.id}
                        onClick={() => {
                          if (!window.confirm('Reject this review? Soft delete - cannot be undone via UI.')) return;
                          void runAction(r.id, adminRejectReview, 'Reject');
                        }}
                        className="inline-flex items-center gap-1 rounded border border-destructive/40 px-2 py-1 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
                      >
                        <X className="h-3 w-3" />
                        Reject
                      </button>
                    )}
                    <button
                      type="button"
                      disabled={!!r.response}
                      onClick={() => setRespondingTo(r)}
                      className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
                      title={r.response ? 'Owner already responded' : 'Respond to this review'}
                    >
                      <MessageSquare className="h-3 w-3" />
                      Respond
                    </button>
                  </div>
                </div>
                {r.body && <p className="text-sm whitespace-pre-wrap">{r.body}</p>}
                {r.response && (
                  <div className="rounded-md border-l-4 border-brand-maroon-700 bg-muted/30 p-2 text-xs">
                    <div className="font-medium text-muted-foreground">Host response</div>
                    <p className="mt-0.5 whitespace-pre-wrap">{r.response.body}</p>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {respondingTo && (
        <RespondModal
          review={respondingTo}
          onClose={() => setRespondingTo(null)}
          onSent={() => {
            setRespondingTo(null);
            void reload();
          }}
        />
      )}
    </div>
  );
};

interface RespondProps {
  readonly review: Review;
  readonly onClose: () => void;
  readonly onSent: () => void;
}

const RespondModal = ({ review, onClose, onSent }: RespondProps) => {
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!body.trim()) {
      setErr('Reply cannot be empty.');
      return;
    }
    setBusy(true);
    setErr(null);
    try {
      await respondToReview(review.id, body.trim());
      onSent();
    } catch (e2) {
      setErr(extractErr(e2, 'Failed to post response.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-lg space-y-4 rounded-lg border border-border bg-background p-6 shadow-lg"
      >
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-base font-medium">Respond to review</h3>
            <p className="text-xs text-muted-foreground">
              From <strong>{review.guestDisplayName}</strong> · {review.rating}/5 stars
            </p>
          </div>
          <button type="button" onClick={onClose} className="rounded-md p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
        {review.body && (
          <p className="rounded-md border border-border bg-muted/30 p-2 text-xs whitespace-pre-wrap">
            {review.body}
          </p>
        )}
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={5}
          maxLength={4000}
          className="w-full rounded-md border border-border bg-background p-3 text-sm"
          placeholder="Thanks for staying with us..."
          required
        />
        <div className="text-right text-xs text-muted-foreground">{body.length} / 4000</div>
        {err && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
            {err}
          </div>
        )}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent">
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy}
            className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            {busy ? 'Posting…' : 'Post response'}
          </button>
        </div>
      </form>
    </div>
  );
};

function extractErr(err: unknown, fallback: string): string {
  if (err instanceof ApiProblemError) return err.problem.detail ?? err.message;
  if (err instanceof Error) return err.message;
  return fallback;
}

export default AdminReviewsPage;
