'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { AlertCircle, ChevronRight, Clock, Home, CalendarCheck } from 'lucide-react';

import { adminListBookings, type AdminBookingSummary } from '@/lib/api/booking';
import { adminListMyProperties, type AdminPropertySummary } from '@/lib/api/catalog';
import { useTentativeBookingPush } from '@/hooks/useTentativeBookingPush';

const AdminDashboardPage = () => {
  const [bookings, setBookings] = useState<readonly AdminBookingSummary[]>([]);
  const [properties, setProperties] = useState<readonly AdminPropertySummary[]>([]);
  const [loading, setLoading] = useState(true);

  const refetchBookings = useCallback(async () => {
    try {
      const bs = await adminListBookings();
      setBookings(bs);
    } catch {
      /* dashboard degrades gracefully */
    }
  }, []);

  useEffect(() => {
    (async () => {
      try {
        const [bs, ps] = await Promise.all([adminListBookings(), adminListMyProperties()]);
        setBookings(bs);
        setProperties(ps);
      } catch {
        // dashboard degrades gracefully — empty cards
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  // Realtime push: when a new tentative booking lands, refetch the canonical
  // list (single source of truth — see SLICE7_PLAN §2.5 step 5). The pill
  // increments naturally on the next render.
  const { connected } = useTentativeBookingPush(() => {
    void refetchBookings();
  });

  // Always-on safety net (§2.11): on tab-focus, refetch once regardless of
  // SignalR state. Catches both "long background + connected" staleness AND
  // "SignalR down + user came back" degradation.
  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        void refetchBookings();
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => document.removeEventListener('visibilitychange', onVisibility);
  }, [refetchBookings]);

  const tentative = bookings.filter((b) => b.status === 'Tentative');
  const confirmed = bookings.filter((b) => b.status === 'Confirmed');
  const publishedProps = properties.filter((p) => p.isActive).length;
  const today = new Date().toISOString().slice(0, 10);
  const todaysCheckins = bookings.filter(
    (b) => b.status === 'Confirmed' && b.checkinDate === today,
  ).length;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <span
          className="inline-flex items-center gap-1 text-xs text-muted-foreground"
          title={connected ? 'Realtime push is connected' : 'Realtime push offline; tab-focus refresh active'}
        >
          <span
            className={`h-2 w-2 rounded-full ${connected ? 'bg-green-500' : 'bg-muted-foreground/40'}`}
          />
          {connected ? 'Live' : 'Polling'}
        </span>
      </div>

      {tentative.length > 0 && (
        <div className="flex items-start justify-between gap-3 rounded-xl border border-yellow-300 bg-yellow-50 p-4 dark:border-yellow-700 dark:bg-yellow-900/20">
          <div className="flex items-start gap-3">
            <AlertCircle className="mt-0.5 h-5 w-5 text-yellow-700 dark:text-yellow-300" aria-hidden />
            <div className="text-sm">
              <p className="font-medium text-yellow-900 dark:text-yellow-100">
                {tentative.length} booking{tentative.length === 1 ? '' : 's'} awaiting your decision
              </p>
              <p className="text-yellow-800 dark:text-yellow-200">
                Confirm or reject before the SLA window expires (auto-resolves every 10 minutes).
              </p>
            </div>
          </div>
          <Link
            href="/admin/bookings?status=Tentative"
            className="inline-flex items-center gap-1 rounded-md bg-yellow-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-yellow-800"
          >
            Review <ChevronRight className="h-3 w-3" aria-hidden />
          </Link>
        </div>
      )}

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Kpi label="Properties" value={loading ? '—' : `${publishedProps}/${properties.length}`} sublabel="published / total" href="/admin/properties" icon={Home} />
        <Kpi label="Tentative" value={loading ? '—' : tentative.length} sublabel="awaiting decision" href="/admin/bookings?status=Tentative" icon={Clock} />
        <Kpi label="Confirmed" value={loading ? '—' : confirmed.length} sublabel="upcoming stays" href="/admin/bookings?status=Confirmed" icon={CalendarCheck} />
        <Kpi label="Today" value={loading ? '—' : todaysCheckins} sublabel="check-ins" href="/admin/bookings" icon={CalendarCheck} />
      </div>

      <div className="rounded-xl border border-border bg-card p-6">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-base font-medium">Recent bookings</h2>
          <Link href="/admin/bookings" className="text-xs text-muted-foreground hover:text-foreground">
            View all →
          </Link>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : bookings.length === 0 ? (
          <p className="text-sm text-muted-foreground">No bookings yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {bookings.slice(0, 6).map((b) => (
              <li key={b.id}>
                <Link
                  href={`/admin/bookings/${b.id}`}
                  className="flex items-center justify-between gap-3 py-3 text-sm hover:bg-muted/40"
                >
                  <div className="flex items-center gap-3">
                    <span
                      className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium ${
                        b.status === 'Tentative'
                          ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300'
                          : b.status === 'Confirmed'
                            ? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300'
                            : 'bg-muted text-muted-foreground'
                      }`}
                    >
                      {b.status}
                    </span>
                    <span>{b.propertyTitle}</span>
                    <span className="text-xs text-muted-foreground">
                      {b.guestDisplayName} · {b.checkinDate} → {b.checkoutDate}
                    </span>
                  </div>
                  <ChevronRight className="h-4 w-4 text-muted-foreground" aria-hidden />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
};

const Kpi = ({
  label,
  value,
  sublabel,
  href,
  icon: Icon,
}: {
  label: string;
  value: string | number;
  sublabel: string;
  href: string;
  icon: typeof Home;
}) => (
  <Link
    href={href}
    className="rounded-xl border border-border bg-card p-4 hover:border-foreground/20"
  >
    <div className="flex items-center justify-between">
      <span className="text-xs text-muted-foreground">{label}</span>
      <Icon className="h-4 w-4 text-muted-foreground" aria-hidden />
    </div>
    <p className="mt-2 text-2xl font-semibold">{value}</p>
    <p className="text-xs text-muted-foreground">{sublabel}</p>
  </Link>
);

export default AdminDashboardPage;
