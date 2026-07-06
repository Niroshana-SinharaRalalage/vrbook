'use client';

import { useEffect, useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Copy, Plus, Trash2, X } from 'lucide-react';
import {
  adminListMyProperties,
  type AdminPropertySummary,
} from '@/lib/api/catalog';
import {
  createAvailabilityBlock,
  deleteAvailabilityBlock,
  getPropertyCalendar,
  type PropertyCalendar,
  type CalendarBookingEntry,
  type CalendarExternalEntry,
  type CalendarBlockEntry,
  type CalendarHoldEntry,
} from '@/lib/api/booking';
import { listChannelFeeds, type ChannelFeed } from '@/lib/api/sync';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import { ApiProblemError } from '@/lib/api/client';

// ---- Date helpers -------------------------------------------------------

const toIso = (d: Date): string => {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
};

const parseIso = (s: string): Date => {
  const parts = s.split('-').map(Number);
  return new Date(parts[0]!, parts[1]! - 1, parts[2]!);
};

const addDays = (d: Date, n: number): Date => {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
};

const startOfMonth = (d: Date): Date => new Date(d.getFullYear(), d.getMonth(), 1);
const startOfNextMonth = (d: Date): Date => new Date(d.getFullYear(), d.getMonth() + 1, 1);

const monthLabel = (d: Date): string =>
  d.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });

// Generate 6 rows × 7 cols of dates covering the month, starting on Sunday.
const buildGrid = (anchor: Date): Date[] => {
  const first = startOfMonth(anchor);
  const offset = first.getDay(); // 0=Sun
  const gridStart = addDays(first, -offset);
  return Array.from({ length: 42 }, (_, i) => addDays(gridStart, i));
};

// ---- Entry typing -------------------------------------------------------

type EntryKind = 'Confirmed' | 'Tentative' | 'External' | 'Block' | 'Hold' | 'AwaitingTurnover';

interface DayEntry {
  readonly kind: EntryKind;
  readonly color: string;       // tailwind bg class
  readonly border?: string;     // optional border for striped
  readonly label: string;
  readonly raw:
    | { type: 'booking'; data: CalendarBookingEntry }
    | { type: 'external'; data: CalendarExternalEntry }
    | { type: 'block'; data: CalendarBlockEntry }
    | { type: 'hold'; data: CalendarHoldEntry };
}

// Half-open [checkin, checkout) — the checkout day is NOT occupied (industry standard).
const overlapsDay = (startIso: string, endIsoExclusive: string, day: Date): boolean => {
  const dayIso = toIso(day);
  return dayIso >= startIso && dayIso < endIsoExclusive;
};

// Slice OPS.M.16 polish — the checkout day of a CheckedOut booking with an
// active turnover window is a single-day overlay (not part of the half-open
// [checkin, checkout) range covered by overlapsDay above). Renders as a
// distinct amber striped chip to signal "same-day new booking is blocked."
const isCheckoutDay = (checkoutIsoExclusive: string, day: Date): boolean =>
  toIso(day) === checkoutIsoExclusive;

const collectEntriesForDay = (cal: PropertyCalendar | null, day: Date): DayEntry[] => {
  if (!cal) return [];
  const out: DayEntry[] = [];

  for (const b of cal.bookings) {
    if (overlapsDay(b.checkin, b.checkout, day)) {
      if (b.status === 'Confirmed' || b.status === 'CheckedIn' || b.status === 'CheckedOut' || b.status === 'Completed') {
        out.push({
          kind: 'Confirmed',
          color: 'bg-blue-500',
          label: `Direct · ${b.reference}`,
          raw: { type: 'booking', data: b },
        });
      } else if (b.status === 'Tentative') {
        out.push({
          kind: 'Tentative',
          color: 'bg-blue-300',
          border: 'ring-1 ring-blue-500 [background-image:repeating-linear-gradient(45deg,transparent_0,transparent_4px,rgba(255,255,255,0.5)_4px,rgba(255,255,255,0.5)_8px)]',
          label: `Tentative · ${b.reference}`,
          raw: { type: 'booking', data: b },
        });
      }
    }
    // Slice OPS.M.16 polish — awaiting-turnover chip on the checkout day
    // itself. Only fires when the backend flag is set (status=CheckedOut
    // and turnover window not yet elapsed); once the booking transitions
    // to Completed, the DTO drops the flag and this chip disappears.
    if (b.awaitingTurnover === true && isCheckoutDay(b.checkout, day)) {
      out.push({
        kind: 'AwaitingTurnover',
        color: 'bg-amber-400',
        border: 'ring-1 ring-amber-600 [background-image:repeating-linear-gradient(45deg,transparent_0,transparent_4px,rgba(255,255,255,0.55)_4px,rgba(255,255,255,0.55)_8px)]',
        label: `Awaiting turnover · ${b.reference}`,
        raw: { type: 'booking', data: b },
      });
    }
  }
  for (const e of cal.externalReservations) {
    if (!overlapsDay(e.checkin, e.checkout, day)) continue;
    out.push({
      kind: 'External',
      color: 'bg-rose-500',
      label: `${e.channel} · ${e.summary ?? 'Reserved'}`,
      raw: { type: 'external', data: e },
    });
  }
  for (const blk of cal.blocks) {
    if (!overlapsDay(blk.startDate, blk.endDate, day)) continue;
    out.push({
      kind: 'Block',
      color: 'bg-gray-400',
      label: `Blocked${blk.reason ? ` · ${blk.reason}` : ''}`,
      raw: { type: 'block', data: blk },
    });
  }
  for (const h of cal.holds) {
    if (!overlapsDay(h.checkin, h.checkout, day)) continue;
    out.push({
      kind: 'Hold',
      color: 'bg-amber-300',
      label: 'Hold (checkout in progress)',
      raw: { type: 'hold', data: h },
    });
  }
  return out;
};

// ---- Page ---------------------------------------------------------------

const AdminCalendarPage = () => {
  // Slice OPS.M.10.2 F11.7.4.7b — initial property + feed lists on
  // useAuthedQuery so the MSAL-readiness gate prevents the cold-mount
  // 401 race. The per-property calendar fetch below stays in a
  // useEffect: by the time the user picks a property, MSAL is ready.
  const propertiesQ = useAuthedQuery<readonly AdminPropertySummary[]>({
    queryKey: ['admin', 'properties', 'mine'],
    queryFn: adminListMyProperties,
  });
  const feedsQ = useAuthedQuery<readonly ChannelFeed[]>({
    queryKey: ['admin', 'channel-feeds'],
    queryFn: listChannelFeeds,
  });
  const properties = propertiesQ.data ?? [];
  const feeds = feedsQ.data ?? [];
  const [propertyId, setPropertyId] = useState<string>('');
  const [anchor, setAnchor] = useState<Date>(startOfMonth(new Date()));
  const [calendar, setCalendar] = useState<PropertyCalendar | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedDay, setSelectedDay] = useState<Date | null>(null);
  const [showBlockForm, setShowBlockForm] = useState(false);

  // Default the picked property to the first one as soon as the list lands.
  useEffect(() => {
    if (!propertyId && properties.length > 0) {
      const first = properties[0];
      if (first) setPropertyId(first.id);
    }
  }, [properties, propertyId]);

  useEffect(() => {
    if (propertiesQ.isError) {
      setError(extractErr(propertiesQ.error, 'Failed to load properties'));
      setLoading(false);
    }
    if (feedsQ.isError) {
      setError(extractErr(feedsQ.error, 'Failed to load channel feeds'));
    }
    if (properties.length === 0 && !propertiesQ.isLoading) {
      setLoading(false);
    }
  }, [propertiesQ.isError, propertiesQ.error, propertiesQ.isLoading, feedsQ.isError, feedsQ.error, properties.length]);

  // Calendar load when property or month changes.
  useEffect(() => {
    if (!propertyId) return;
    setLoading(true);
    setError(null);
    const from = toIso(startOfMonth(anchor));
    const to = toIso(startOfNextMonth(anchor));
    getPropertyCalendar(propertyId, from, to)
      .then((c) => setCalendar(c))
      .catch((err) => setError(extractErr(err, 'Failed to load calendar')))
      .finally(() => setLoading(false));
  }, [propertyId, anchor]);

  const grid = useMemo(() => buildGrid(anchor), [anchor]);
  const currentMonth = anchor.getMonth();

  const outboundFeed = useMemo(
    () => feeds.find((f) => f.propertyId === propertyId),
    [feeds, propertyId],
  );

  if (propertiesQ.needsSignIn) {
    return <SignInGate title="Sign in to view your calendar" />;
  }

  const reloadCalendar = async () => {
    if (!propertyId) return;
    const from = toIso(startOfMonth(anchor));
    const to = toIso(startOfNextMonth(anchor));
    try {
      const c = await getPropertyCalendar(propertyId, from, to);
      setCalendar(c);
    } catch (err) {
      setError(extractErr(err, 'Failed to reload calendar'));
    }
  };

  if (properties.length === 0 && !loading) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Calendar</h1>
        <div className="rounded-md border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
          You don&apos;t have any properties yet.{' '}
          <a href="/admin/properties/new" className="text-brand-maroon-700 underline">
            Add your first property
          </a>{' '}
          to see its calendar.
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Calendar</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Direct bookings, AirBnB-imported reservations, owner blocks, and checkout
            holds in one view. iCal feeds poll every 5 minutes; new entries land here
            within ~6 minutes of arrival.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setShowBlockForm(true)}
          disabled={!propertyId}
          className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-800 disabled:opacity-50"
        >
          <Plus className="h-4 w-4" />
          Block dates
        </button>
      </header>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-2">
        <label className="block text-sm">
          <span className="block text-xs font-medium text-muted-foreground">Property</span>
          <select
            value={propertyId}
            onChange={(e) => setPropertyId(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-2 text-sm"
          >
            {properties.map((p) => (
              <option key={p.id} value={p.id}>{p.title}</option>
            ))}
          </select>
        </label>

        {outboundFeed && (
          <div className="rounded-md border border-border bg-card p-3 text-xs">
            <div className="flex items-center justify-between">
              <span className="font-medium">Share calendar with AirBnB</span>
              <button
                type="button"
                onClick={() => void navigator.clipboard.writeText(outboundFeed.outboundFeedUrl)}
                className="inline-flex items-center gap-1 rounded border border-border px-2 py-0.5 text-xs hover:bg-accent"
              >
                <Copy className="h-3 w-3" />
                Copy URL
              </button>
            </div>
            <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
              {outboundFeed.outboundFeedUrl}
            </div>
            <details className="mt-1.5">
              <summary className="cursor-pointer text-muted-foreground">How to subscribe AirBnB to this</summary>
              <ol className="ml-4 mt-1 list-decimal space-y-0.5 text-muted-foreground">
                <li>Open your AirBnB host calendar.</li>
                <li>Click <b>Availability</b> → <b>Sync calendars</b> → <b>Import calendar</b>.</li>
                <li>Paste the URL above and name it &ldquo;VrBook&rdquo;.</li>
                <li>AirBnB will poll it every 2&ndash;3 hours and block these dates on its side.</li>
              </ol>
            </details>
          </div>
        )}
      </div>

      <Legend />

      <section className="rounded-md border border-border bg-card">
        <div className="flex items-center justify-between border-b border-border px-4 py-2">
          <button
            type="button"
            onClick={() => setAnchor(addDays(anchor, -28))}
            className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm hover:bg-accent"
            title="Previous month"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <div className="text-sm font-medium">
            {monthLabel(anchor)}
            {loading && <span className="ml-2 text-xs text-muted-foreground">loading…</span>}
          </div>
          <button
            type="button"
            onClick={() => setAnchor(startOfNextMonth(anchor))}
            className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm hover:bg-accent"
            title="Next month"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>

        <div className="grid grid-cols-7 border-b border-border bg-muted/30 text-center text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
          {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((d) => (
            <div key={d} className="py-1">{d}</div>
          ))}
        </div>

        <div className="grid grid-cols-7">
          {grid.map((day) => {
            const inMonth = day.getMonth() === currentMonth;
            const entries = collectEntriesForDay(calendar, day);
            const isToday = toIso(day) === toIso(new Date());
            return (
              <button
                key={day.toISOString()}
                type="button"
                onClick={() => setSelectedDay(day)}
                className={`min-h-[88px] border-b border-r border-border p-1 text-left text-xs transition hover:bg-accent/40 ${inMonth ? 'bg-background' : 'bg-muted/30 text-muted-foreground'} ${isToday ? 'ring-1 ring-inset ring-brand-maroon-700' : ''}`}
              >
                <div className="flex items-center justify-between">
                  <span className={`text-[11px] font-medium ${isToday ? 'text-brand-maroon-700' : ''}`}>
                    {day.getDate()}
                  </span>
                  {entries.length > 2 && (
                    <span className="text-[10px] text-muted-foreground">+{entries.length - 2}</span>
                  )}
                </div>
                <div className="mt-1 space-y-0.5">
                  {entries.slice(0, 2).map((e, i) => (
                    <div
                      key={i}
                      className={`truncate rounded-sm px-1 py-0.5 text-[10px] font-medium text-white ${e.color} ${e.border ?? ''}`}
                      title={e.label}
                    >
                      {e.label}
                    </div>
                  ))}
                </div>
              </button>
            );
          })}
        </div>
      </section>

      {selectedDay && (
        <DayDetailPanel
          day={selectedDay}
          entries={collectEntriesForDay(calendar, selectedDay)}
          onClose={() => setSelectedDay(null)}
          onBlockDeleted={() => {
            setSelectedDay(null);
            void reloadCalendar();
          }}
          propertyId={propertyId}
        />
      )}

      {showBlockForm && propertyId && (
        <BlockDatesForm
          propertyId={propertyId}
          onClose={() => setShowBlockForm(false)}
          onCreated={() => {
            setShowBlockForm(false);
            void reloadCalendar();
          }}
        />
      )}
    </div>
  );
};

// ---- Legend -------------------------------------------------------------

const Legend = () => (
  <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
    <LegendChip className="bg-blue-500" label="Direct (Confirmed)" />
    <LegendChip className="bg-blue-300" label="Tentative" />
    <LegendChip className="bg-rose-500" label="External (AirBnB / VRBO)" />
    <LegendChip className="bg-gray-400" label="Blocked (owner)" />
    <LegendChip className="bg-amber-300" label="Hold (checkout in progress)" />
    <LegendChip className="bg-amber-400" label="Awaiting turnover (housekeeping)" />
  </div>
);

const LegendChip = ({ className, label }: { className: string; label: string }) => (
  <span className="inline-flex items-center gap-1.5">
    <span className={`inline-block h-3 w-3 rounded-sm ${className}`} />
    {label}
  </span>
);

// ---- Day detail panel ---------------------------------------------------

interface DayDetailProps {
  readonly day: Date;
  readonly entries: DayEntry[];
  readonly onClose: () => void;
  readonly onBlockDeleted: () => void;
  readonly propertyId: string;
}

const DayDetailPanel = ({ day, entries, onClose, onBlockDeleted, propertyId }: DayDetailProps) => {
  const [busyBlockId, setBusyBlockId] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const removeBlock = async (blockId: string) => {
    if (!window.confirm('Remove this block? Guests will be able to book these dates again.')) {
      return;
    }
    setBusyBlockId(blockId);
    setErr(null);
    try {
      await deleteAvailabilityBlock(propertyId, blockId);
      onBlockDeleted();
    } catch (e) {
      setErr(extractErr(e, 'Failed to remove block'));
    } finally {
      setBusyBlockId(null);
    }
  };

  return (
    <div className="fixed inset-0 z-40 flex items-start justify-end bg-black/40 p-4">
      <aside className="w-full max-w-md space-y-3 rounded-lg border border-border bg-background p-4 shadow-xl">
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-sm font-medium">
              {day.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })}
            </h3>
            <p className="text-xs text-muted-foreground">{entries.length} entr{entries.length === 1 ? 'y' : 'ies'}</p>
          </div>
          <button type="button" onClick={onClose} className="rounded-md p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
        {err && <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{err}</div>}
        {entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nothing on this day. Click <b>Block dates</b> above to reserve it for maintenance or personal use.</p>
        ) : (
          <ul className="space-y-2">
            {entries.map((e, i) => (
              <li key={i} className="rounded-md border border-border p-2 text-xs">
                <div className="flex items-center gap-2">
                  <span className={`inline-block h-2.5 w-2.5 rounded-sm ${e.color}`} />
                  <span className="font-medium">{e.label}</span>
                </div>
                {e.raw.type === 'booking' && (
                  <div className="mt-1 text-muted-foreground">
                    {e.raw.data.checkin} → {e.raw.data.checkout} · {e.raw.data.guestDisplayName}
                    {e.kind === 'AwaitingTurnover' && (
                      <div className="mt-1 text-amber-700">
                        Housekeeping pending. Same-day new booking blocked until the operator
                        confirms turnover via the <b>Complete now</b> action on the booking
                        detail page.
                      </div>
                    )}
                  </div>
                )}
                {e.raw.type === 'external' && (
                  <div className="mt-1 text-muted-foreground">
                    {e.raw.data.checkin} → {e.raw.data.checkout}
                  </div>
                )}
                {e.raw.type === 'block' && (() => {
                  const blk = e.raw.data;
                  return (
                    <div className="mt-1 flex items-center justify-between text-muted-foreground">
                      <span>{blk.startDate} → {blk.endDate}</span>
                      <button
                        type="button"
                        onClick={() => void removeBlock(blk.blockId)}
                        disabled={busyBlockId === blk.blockId}
                        className="inline-flex items-center gap-1 rounded border border-destructive/40 px-2 py-0.5 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
                      >
                        <Trash2 className="h-3 w-3" />
                        {busyBlockId === blk.blockId ? 'Removing…' : 'Remove'}
                      </button>
                    </div>
                  );
                })()}
                {e.raw.type === 'hold' && (
                  <div className="mt-1 text-muted-foreground">
                    Expires {new Date(e.raw.data.expiresAt).toLocaleString()}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </aside>
    </div>
  );
};

// ---- Block dates modal --------------------------------------------------

interface BlockDatesProps {
  readonly propertyId: string;
  readonly onClose: () => void;
  readonly onCreated: () => void;
}

const BlockDatesForm = ({ propertyId, onClose, onCreated }: BlockDatesProps) => {
  const today = toIso(new Date());
  const [startDate, setStartDate] = useState(today);
  const [endDate, setEndDate] = useState(toIso(addDays(new Date(), 2)));
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (parseIso(endDate) <= parseIso(startDate)) {
      setErr('End date must be after start date.');
      return;
    }
    setBusy(true);
    setErr(null);
    try {
      await createAvailabilityBlock(propertyId, {
        startDate,
        endDate,
        reason: reason.trim() || null,
      });
      onCreated();
    } catch (e2) {
      setErr(extractErr(e2, 'Failed to create block'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-md space-y-3 rounded-lg border border-border bg-background p-6 shadow-lg"
      >
        <div className="flex items-start justify-between">
          <h3 className="text-base font-medium">Block dates</h3>
          <button type="button" onClick={onClose} className="rounded-md p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="text-xs text-muted-foreground">
          Reserve these dates for maintenance, owner stay, or any reason outside the booking system.
          Guests will not be able to book overlapping dates. Dates are half-open: end date is the day
          you&apos;re free again.
        </p>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Start (first blocked night)</span>
          <input
            type="date"
            value={startDate}
            onChange={(e) => setStartDate(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
            required
          />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">End (first free day after the block)</span>
          <input
            type="date"
            value={endDate}
            onChange={(e) => setEndDate(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
            required
          />
        </label>
        <label className="block text-xs">
          <span className="block text-muted-foreground">Reason (optional, 200 chars max)</span>
          <input
            type="text"
            maxLength={200}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
            placeholder="e.g. plumbing repairs"
          />
        </label>
        {err && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
            {err}
          </div>
        )}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent">
            Cancel
          </button>
          <button type="submit" disabled={busy} className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50">
            {busy ? 'Blocking…' : 'Block dates'}
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

export default AdminCalendarPage;
