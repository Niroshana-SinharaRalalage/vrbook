'use client';

import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangle, Copy, Pause, Play, Plus, RefreshCw, Trash2, X } from 'lucide-react';
import {
  createChannelFeed,
  deleteChannelFeed,
  listChannelFeeds,
  listSyncConflicts,
  resolveSyncConflict,
  updateChannelFeed,
  type ChannelFeed,
  type SyncConflict,
  type SyncConflictResolution,
} from '@/lib/api/sync';
import { ApiProblemError } from '@/lib/api/client';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';

// Slice OPS.M.10.2 F11.7.4.7b — feeds + conflicts on useAuthedQuery;
// mutations invalidate.
const FEEDS_QK = ['admin', 'channel-feeds'] as const;
const CONFLICTS_QK = ['admin', 'sync-conflicts'] as const;

const AdminSyncPage = () => {
  const qc = useQueryClient();
  const feedsQ = useAuthedQuery<readonly ChannelFeed[]>({
    queryKey: [...FEEDS_QK],
    queryFn: listChannelFeeds,
  });
  const conflictsQ = useAuthedQuery<readonly SyncConflict[]>({
    queryKey: [...CONFLICTS_QK],
    queryFn: listSyncConflicts,
  });
  const feeds = feedsQ.data ?? [];
  const conflicts = conflictsQ.data ?? [];
  const loading = feedsQ.isLoading || conflictsQ.isLoading;
  const needsSignIn = feedsQ.needsSignIn || conflictsQ.needsSignIn;
  const [error, setError] = useState<string | null>(null);
  const queryErrorMsg = feedsQ.isError
    ? extractErr(feedsQ.error, 'Failed to load')
    : conflictsQ.isError
      ? extractErr(conflictsQ.error, 'Failed to load')
      : null;
  const displayError = error ?? queryErrorMsg;
  const reload = async () => {
    setError(null);
    await Promise.all([
      qc.invalidateQueries({ queryKey: [...FEEDS_QK] }),
      qc.invalidateQueries({ queryKey: [...CONFLICTS_QK] }),
    ]);
  };
  const [showCreate, setShowCreate] = useState(false);
  const [resolving, setResolving] = useState<SyncConflict | null>(null);

  const onToggle = async (f: ChannelFeed) => {
    try {
      await updateChannelFeed(f.id, {
        inboundUrl: f.inboundUrl,
        pollIntervalMinutes: f.pollIntervalMinutes,
        isEnabled: !f.isEnabled,
      });
      await reload();
    } catch (err) {
      setError(extractErr(err, 'Toggle failed'));
    }
  };

  const onDelete = async (f: ChannelFeed) => {
    if (!window.confirm(`Delete feed for ${f.channel}?\n\nThis stops polling. External reservations already imported stay in place.`)) {
      return;
    }
    try {
      await deleteChannelFeed(f.id);
      await reload();
    } catch (err) {
      setError(extractErr(err, 'Delete failed'));
    }
  };


  const pendingConflicts = useMemo(
    () => conflicts.filter((c) => c.resolution === 'Pending'),
    [conflicts],
  );

  if (needsSignIn) {
    return <SignInGate title="Sign in to manage sync" />;
  }

  return (
    <div className="space-y-8">
      <header className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Sync</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Channel feeds + the conflict queue. Feeds are polled every 5 minutes by the Sync Worker.
            Outbound URL is the iCal subscription you share with AirBnB / Google Calendar.
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

      {/* ---- Pending conflicts queue ---- */}
      <section className="space-y-3">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-5 w-5 text-amber-600" />
          <h2 className="text-lg font-medium">
            Pending conflicts {pendingConflicts.length > 0 && (
              <span className="ml-2 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-950 dark:text-amber-200">
                {pendingConflicts.length}
              </span>
            )}
          </h2>
        </div>
        {pendingConflicts.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {loading ? 'Loading…' : 'No conflicts. Sync is healthy.'}
          </p>
        ) : (
          <ul className="divide-y divide-border rounded-md border border-border">
            {pendingConflicts.map((c) => (
              <li key={c.id} className="px-4 py-3 text-sm">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <div className="text-xs font-medium text-muted-foreground">External (AirBnB-side)</div>
                    <div className="font-medium">{c.externalSummary || 'Reserved'}</div>
                    <div className="text-xs text-muted-foreground">{c.externalCheckin} → {c.externalCheckout}</div>
                  </div>
                  <div>
                    <div className="text-xs font-medium text-muted-foreground">Direct booking</div>
                    <div className="font-medium">{c.bookingReference}</div>
                    <div className="text-xs text-muted-foreground">{c.bookingCheckin} → {c.bookingCheckout}</div>
                  </div>
                </div>
                <div className="mt-2 flex justify-end">
                  <button
                    type="button"
                    onClick={() => setResolving(c)}
                    className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-maroon-800"
                  >
                    Resolve…
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* ---- Channel feeds ---- */}
      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-medium">
            Channel feeds {feeds.length > 0 && <span className="text-muted-foreground">({feeds.length})</span>}
          </h2>
          <button
            type="button"
            onClick={() => setShowCreate(true)}
            className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800"
          >
            <Plus className="h-4 w-4" />
            Add feed
          </button>
        </div>

        {showCreate && (
          <CreateFeedForm
            onClose={() => setShowCreate(false)}
            onCreated={() => {
              setShowCreate(false);
              void reload();
            }}
          />
        )}

        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : feeds.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No feeds yet. Add one to start polling an external calendar (AirBnB / VRBO / Booking.com).
          </p>
        ) : (
          <ul className="divide-y divide-border rounded-md border border-border">
            {feeds.map((f) => (
              <li key={f.id} className="px-4 py-3 text-sm">
                <div className="flex items-start gap-3">
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <span className="font-medium">{f.channel}</span>
                      {!f.isEnabled && (
                        <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
                          Paused
                        </span>
                      )}
                      {f.lastError && (
                        <span className="inline-flex items-center gap-1 rounded-full bg-destructive/10 px-2 py-0.5 text-xs text-destructive">
                          <AlertTriangle className="h-3 w-3" />
                          {f.lastError}
                        </span>
                      )}
                    </div>
                    <div className="mt-0.5 text-xs text-muted-foreground">
                      every {f.pollIntervalMinutes}m
                      {f.lastSuccessAt && ` · last success ${new Date(f.lastSuccessAt).toLocaleString()}`}
                    </div>

                    {/* Slice 3 polish: outbound URL is the thing the owner actually
                        needs to copy back to AirBnB. Pull it out of the details. */}
                    <div className="mt-2 rounded-md border border-emerald-200 bg-emerald-50/60 p-2 dark:border-emerald-900 dark:bg-emerald-950/40">
                      <div className="flex items-center justify-between gap-2 text-xs">
                        <span className="font-medium text-emerald-900 dark:text-emerald-200">
                          Outbound iCal (give this to AirBnB)
                        </span>
                        <button
                          type="button"
                          onClick={() => void navigator.clipboard.writeText(f.outboundFeedUrl)}
                          className="inline-flex items-center gap-1 rounded border border-emerald-300 bg-white px-2 py-0.5 text-xs font-medium text-emerald-900 hover:bg-emerald-100 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-200"
                        >
                          <Copy className="h-3 w-3" />
                          Copy URL
                        </button>
                      </div>
                      <div className="mt-1 break-all font-mono text-[11px] text-emerald-900/80 dark:text-emerald-200/80">
                        {f.outboundFeedUrl}
                      </div>
                      <details className="mt-1 text-xs text-emerald-900/80 dark:text-emerald-200/80">
                        <summary className="cursor-pointer">How to subscribe AirBnB to this URL</summary>
                        <ol className="ml-4 mt-1 list-decimal space-y-0.5">
                          <li>Open the AirBnB host calendar for this listing.</li>
                          <li>Go to <b>Availability</b> → <b>Sync calendars</b> → <b>Import calendar</b>.</li>
                          <li>Paste the URL above. Name it &ldquo;VrBook&rdquo;.</li>
                          <li>AirBnB will block these dates on its side within 2&ndash;3 hours.</li>
                        </ol>
                      </details>
                    </div>

                    <details className="mt-1.5 text-xs">
                      <summary className="cursor-pointer text-muted-foreground">Inbound URL (you pasted this from AirBnB)</summary>
                      <div className="mt-1 break-all pl-4 font-mono text-xs">{f.inboundUrl}</div>
                    </details>
                  </div>
                  <button
                    type="button"
                    onClick={() => void onToggle(f)}
                    className={
                      f.isEnabled
                        ? 'inline-flex items-center gap-1 rounded-md border border-amber-300 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-800 hover:bg-amber-100 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-200'
                        : 'inline-flex items-center gap-1 rounded-md border border-emerald-300 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800 hover:bg-emerald-100 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-200'
                    }
                  >
                    {f.isEnabled ? <Pause className="h-3 w-3" /> : <Play className="h-3 w-3" />}
                    {f.isEnabled ? 'Pause' : 'Resume'}
                  </button>
                  <button
                    type="button"
                    onClick={() => void onDelete(f)}
                    className="inline-flex items-center gap-1 rounded-md border border-destructive/40 px-2.5 py-1 text-xs font-medium text-destructive hover:bg-destructive/10"
                  >
                    <Trash2 className="h-3 w-3" />
                    Delete
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      {resolving && (
        <ResolveModal
          conflict={resolving}
          onClose={() => setResolving(null)}
          onResolved={() => {
            setResolving(null);
            void reload();
          }}
        />
      )}
    </div>
  );
};

// ---- Create feed form ---------------------------------------------------

interface CreateFeedProps {
  readonly onClose: () => void;
  readonly onCreated: (f: ChannelFeed) => void;
}

const CreateFeedForm = ({ onClose, onCreated }: CreateFeedProps) => {
  const [propertyId, setPropertyId] = useState('');
  const [inboundUrl, setInboundUrl] = useState('');
  const [interval, setInterval] = useState(30);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      // ChannelKind on the wire is the integer enum value: AirBnb=0.
      const f = await createChannelFeed({
        propertyId,
        channel: 0,
        inboundUrl,
        pollIntervalMinutes: interval,
      });
      onCreated(f);
    } catch (err2) {
      setErr(extractErr(err2, 'Create failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={onSubmit} className="space-y-3 rounded-md border border-border bg-card p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">New AirBnB feed</h3>
        <button type="button" onClick={onClose} className="rounded-md p-1 text-muted-foreground hover:bg-accent">
          <X className="h-4 w-4" />
        </button>
      </div>
      <label className="block text-xs">
        <span className="block text-muted-foreground">Property Id</span>
        <input
          value={propertyId}
          onChange={(e) => setPropertyId(e.target.value)}
          className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 font-mono text-sm"
          required
          placeholder="00000000-0000-0000-0000-000000000000"
        />
      </label>
      <label className="block text-xs">
        <span className="block text-muted-foreground">AirBnB iCal URL</span>
        <input
          value={inboundUrl}
          onChange={(e) => setInboundUrl(e.target.value)}
          className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
          required
          placeholder="https://www.airbnb.com/calendar/ical/…ics?s=…"
        />
      </label>
      <label className="block text-xs">
        <span className="block text-muted-foreground">Poll every (min)</span>
        <input
          type="number"
          min={5}
          value={interval}
          onChange={(e) => setInterval(Number(e.target.value))}
          className="mt-1 w-32 rounded-md border border-border bg-background px-2 py-1 text-sm"
        />
      </label>
      {err && <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{err}</div>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent">
          Cancel
        </button>
        <button type="submit" disabled={busy} className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50">
          {busy ? 'Creating…' : 'Create feed'}
        </button>
      </div>
    </form>
  );
};

// ---- Resolve modal ------------------------------------------------------

interface ResolveProps {
  readonly conflict: SyncConflict;
  readonly onClose: () => void;
  readonly onResolved: () => void;
}

const RESOLUTIONS: { value: SyncConflictResolution; label: string; description: string }[] = [
  { value: 'OwnerKeptDirect', label: 'Keep direct booking', description: 'Ignore the external entry. Use this when AirBnB is showing a stale block.' },
  { value: 'OwnerCancelledDirect', label: 'Cancel direct booking', description: 'AirBnB has the legitimate reservation. Refund the direct guest per policy.' },
  { value: 'ManualOverride', label: 'Manual override', description: 'Resolving off-platform (calling the guest). Parks the conflict.' },
];

// The wire enum: Pending=0, OwnerKeptDirect=1, OwnerCancelledDirect=2, AutoCancelled=3, ManualOverride=4.
const RESOLUTION_INT: Record<SyncConflictResolution, number> = {
  Pending: 0,
  OwnerKeptDirect: 1,
  OwnerCancelledDirect: 2,
  AutoCancelled: 3,
  ManualOverride: 4,
};

const ResolveModal = ({ conflict, onClose, onResolved }: ResolveProps) => {
  const [pick, setPick] = useState<SyncConflictResolution>('OwnerKeptDirect');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await resolveSyncConflict(conflict.id, { resolution: RESOLUTION_INT[pick], notes });
      onResolved();
    } catch (err2) {
      setErr(extractErr(err2, 'Resolve failed'));
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
            <h3 className="text-lg font-medium">Resolve conflict</h3>
            <p className="mt-1 text-xs text-muted-foreground">
              External {conflict.externalCheckin} → {conflict.externalCheckout} vs direct {conflict.bookingReference} ({conflict.bookingCheckin} → {conflict.bookingCheckout})
            </p>
          </div>
          <button type="button" onClick={onClose} className="rounded-md p-1 text-muted-foreground hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
        <fieldset className="space-y-2">
          {RESOLUTIONS.map((r) => (
            <label key={r.value} className="flex cursor-pointer items-start gap-2 rounded-md border border-border p-2 hover:bg-accent">
              <input
                type="radio"
                name="resolution"
                value={r.value}
                checked={pick === r.value}
                onChange={() => setPick(r.value)}
                className="mt-0.5"
              />
              <div>
                <div className="text-sm font-medium">{r.label}</div>
                <div className="text-xs text-muted-foreground">{r.description}</div>
              </div>
            </label>
          ))}
        </fieldset>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Notes (optional)</span>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={3}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
            placeholder="Optional context for the audit trail…"
          />
        </label>
        {err && <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{err}</div>}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent">
            Cancel
          </button>
          <button type="submit" disabled={busy} className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50">
            {busy ? 'Saving…' : 'Resolve'}
          </button>
        </div>
      </form>
    </div>
  );
};

// ---- Error helper -------------------------------------------------------
function extractErr(err: unknown, fallback: string): string {
  if (err instanceof ApiProblemError) return err.problem.detail ?? err.message;
  if (err instanceof Error) return err.message;
  return fallback;
}

export default AdminSyncPage;
