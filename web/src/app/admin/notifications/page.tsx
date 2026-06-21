'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, Clock, Mail, RefreshCw, RotateCcw, Skull, X } from 'lucide-react';
import {
  adminListNotifications,
  adminRetryNotification,
  type NotificationLog,
  type NotificationStatus,
} from '@/lib/api/notifications';
import { ApiProblemError } from '@/lib/api/client';

const STATUS_FILTERS: { value: NotificationStatus | 'All'; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Queued', label: 'Queued' },
  { value: 'Sending', label: 'Sending' },
  { value: 'Sent', label: 'Sent' },
  { value: 'Failed', label: 'Failed' },
  { value: 'DeadLetter', label: 'Dead-letter' },
];

const STATUS_STYLES: Record<NotificationStatus, { className: string; Icon: React.ComponentType<{ className?: string }> }> = {
  Queued: { className: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200', Icon: Clock },
  Sending: { className: 'bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200', Icon: Mail },
  Sent: { className: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200', Icon: CheckCircle2 },
  Failed: { className: 'bg-orange-100 text-orange-800 dark:bg-orange-950 dark:text-orange-200', Icon: AlertTriangle },
  DeadLetter: { className: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200', Icon: Skull },
};

const AdminNotificationsPage = () => {
  const [filter, setFilter] = useState<NotificationStatus | 'All'>('All');
  const [rows, setRows] = useState<readonly NotificationLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<NotificationLog | null>(null);
  const [busyRetryId, setBusyRetryId] = useState<string | null>(null);

  const reload = async () => {
    setError(null);
    setLoading(true);
    try {
      const data = await adminListNotifications(filter === 'All' ? undefined : filter);
      setRows(data);
    } catch (err) {
      setError(extractErr(err, 'Failed to load notifications.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filter]);

  const counts = useMemo(() => {
    const totals: Partial<Record<NotificationStatus, number>> = {};
    for (const r of rows) {
      totals[r.status] = (totals[r.status] ?? 0) + 1;
    }
    return totals;
  }, [rows]);

  const onRetry = async (row: NotificationLog) => {
    setBusyRetryId(row.id);
    setError(null);
    try {
      await adminRetryNotification(row.id);
      await reload();
      if (selected?.id === row.id) {
        setSelected({ ...row, status: 'Queued', retryCount: 0, lastError: null });
      }
    } catch (err) {
      setError(extractErr(err, 'Retry failed.'));
    } finally {
      setBusyRetryId(null);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Notifications</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Every outbound email queued by booking lifecycle events. The dispatch worker drains
            Queued rows every 2 minutes. Failed and dead-letter rows can be retried — that
            resets the row to Queued so the next worker tick picks it up.
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

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
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
              {f.value !== 'All' && counts[f.value as NotificationStatus] != null && (
                <span className="ml-1.5 text-[10px] opacity-80">{counts[f.value as NotificationStatus]}</span>
              )}
            </button>
          );
        })}
      </div>

      <section className="rounded-md border border-border bg-card">
        {loading ? (
          <p className="px-4 py-6 text-sm text-muted-foreground">Loading…</p>
        ) : rows.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted-foreground">
            No notifications match the current filter.
          </p>
        ) : (
          <table className="w-full text-sm">
            <thead className="border-b border-border bg-muted/30 text-left text-[11px] uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-2 font-medium">Status</th>
                <th className="px-4 py-2 font-medium">Kind</th>
                <th className="px-4 py-2 font-medium">Recipient</th>
                <th className="px-4 py-2 font-medium">Subject</th>
                <th className="px-4 py-2 font-medium">Created</th>
                <th className="px-4 py-2 font-medium">Retries</th>
                <th className="px-4 py-2"></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => {
                const s = STATUS_STYLES[r.status];
                return (
                  <tr
                    key={r.id}
                    className="cursor-pointer border-b border-border/50 hover:bg-accent/30"
                    onClick={() => setSelected(r)}
                  >
                    <td className="px-4 py-2">
                      <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] ${s.className}`}>
                        <s.Icon className="h-3 w-3" />
                        {r.status}
                      </span>
                    </td>
                    <td className="px-4 py-2 text-xs font-mono">{r.kind}</td>
                    <td className="px-4 py-2 text-xs">{r.recipientEmail}</td>
                    <td className="px-4 py-2 truncate max-w-[260px]">{r.subject}</td>
                    <td className="px-4 py-2 text-xs text-muted-foreground">
                      {new Date(r.createdAt).toLocaleString()}
                    </td>
                    <td className="px-4 py-2 text-xs">{r.retryCount}</td>
                    <td className="px-4 py-2 text-right" onClick={(e) => e.stopPropagation()}>
                      {(r.status === 'Failed' || r.status === 'DeadLetter') && (
                        <button
                          type="button"
                          onClick={() => void onRetry(r)}
                          disabled={busyRetryId === r.id}
                          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                        >
                          <RotateCcw className="h-3 w-3" />
                          {busyRetryId === r.id ? 'Queuing…' : 'Retry'}
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>

      {selected && (
        <DetailDrawer
          row={selected}
          onClose={() => setSelected(null)}
          onRetry={() => void onRetry(selected)}
          busy={busyRetryId === selected.id}
        />
      )}
    </div>
  );
};

interface DetailDrawerProps {
  readonly row: NotificationLog;
  readonly onClose: () => void;
  readonly onRetry: () => void;
  readonly busy: boolean;
}

const DetailDrawer = ({ row, onClose, onRetry, busy }: DetailDrawerProps) => (
  <div className="fixed inset-0 z-40 flex items-start justify-end bg-black/40 p-4">
    <aside className="w-full max-w-xl space-y-3 rounded-lg border border-border bg-background p-4 shadow-xl">
      <div className="flex items-start justify-between">
        <div>
          <h3 className="text-sm font-medium">{row.subject}</h3>
          <p className="text-xs text-muted-foreground">{row.kind} → {row.recipientEmail}</p>
        </div>
        <button type="button" onClick={onClose} className="rounded-md p-1 hover:bg-accent">
          <X className="h-4 w-4" />
        </button>
      </div>

      <dl className="grid grid-cols-2 gap-2 rounded-md border border-border bg-muted/30 p-2 text-xs">
        <div><dt className="text-muted-foreground">Status</dt><dd>{row.status}</dd></div>
        <div><dt className="text-muted-foreground">Retries</dt><dd>{row.retryCount}</dd></div>
        <div><dt className="text-muted-foreground">Created</dt><dd>{new Date(row.createdAt).toLocaleString()}</dd></div>
        <div><dt className="text-muted-foreground">Sent</dt><dd>{row.sentAt ? new Date(row.sentAt).toLocaleString() : '—'}</dd></div>
        <div className="col-span-2"><dt className="text-muted-foreground">Id</dt><dd className="break-all font-mono text-[11px]">{row.id}</dd></div>
      </dl>

      {row.lastError && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
          <div className="font-medium">Last error</div>
          <div className="mt-1 break-all">{row.lastError}</div>
        </div>
      )}

      {(row.status === 'Failed' || row.status === 'DeadLetter') && (
        <div className="flex justify-end">
          <button
            type="button"
            onClick={onRetry}
            disabled={busy}
            className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            <RotateCcw className="h-3 w-3" />
            {busy ? 'Queuing…' : 'Retry now'}
          </button>
        </div>
      )}
    </aside>
  </div>
);

function extractErr(err: unknown, fallback: string): string {
  if (err instanceof ApiProblemError) return err.problem.detail ?? err.message;
  if (err instanceof Error) return err.message;
  return fallback;
}

export default AdminNotificationsPage;
