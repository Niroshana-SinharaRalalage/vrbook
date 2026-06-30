'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import dynamic from 'next/dynamic';
import { Download, RefreshCw } from 'lucide-react';
import { adminListMyProperties, type AdminPropertySummary } from '@/lib/api/catalog';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import {
  getAdrReport,
  getOccupancyReport,
  getRevenueReport,
  getSourceReport,
  type AdrReport,
  type OccupancyReport,
  type ReportParams,
  type RevenueReport,
  type SourceReport,
} from '@/lib/api/reports';
import { ApiProblemError } from '@/lib/api/client';
import { downloadCsv } from '@/lib/csv';
import ReportFilters, { type ReportFiltersValue } from '@/components/reports/ReportFilters';

// Dynamic-import each chart so the inactive tabs don't pull recharts into
// the /admin/reports initial bundle (SLICE7_PLAN §2.9).
const OccupancyChart = dynamic(() => import('@/components/reports/OccupancyChart'), { ssr: false });
const RevenueChart = dynamic(() => import('@/components/reports/RevenueChart'), { ssr: false });
const AdrChart = dynamic(() => import('@/components/reports/AdrChart'), { ssr: false });
const SourceChart = dynamic(() => import('@/components/reports/SourceChart'), { ssr: false });

type TabKey = 'occupancy' | 'revenue' | 'adr' | 'source';

const TABS: { key: TabKey; label: string }[] = [
  { key: 'occupancy', label: 'Occupancy' },
  { key: 'revenue', label: 'Revenue' },
  { key: 'adr', label: 'ADR' },
  { key: 'source', label: 'Source' },
];

const today = (): string => {
  const d = new Date();
  return d.toISOString().slice(0, 10);
};

const daysAgo = (n: number): string => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
};

const extractErr = (e: unknown, fallback: string): string => {
  if (e instanceof ApiProblemError) return e.problem.detail ?? e.message;
  if (e instanceof Error) return e.message;
  return fallback;
};

const AdminReportsBody = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const tabFromQuery = (searchParams.get('tab') as TabKey | null) ?? 'occupancy';
  const tabKey: TabKey = TABS.some((t) => t.key === tabFromQuery) ? tabFromQuery : 'occupancy';

  const propertiesQ = useAuthedQuery<readonly AdminPropertySummary[]>({
    queryKey: ['admin', 'properties', 'mine'],
    queryFn: adminListMyProperties,
  });
  const properties = propertiesQ.data ?? [];
  const [filters, setFilters] = useState<ReportFiltersValue>({
    from: daysAgo(30),
    to: today(),
    propertyId: null,
  });

  const [occupancy, setOccupancy] = useState<OccupancyReport | null>(null);
  const [revenue, setRevenue] = useState<RevenueReport | null>(null);
  const [adr, setAdr] = useState<AdrReport | null>(null);
  const [source, setSource] = useState<SourceReport | null>(null);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // properties moved to useAuthedQuery above; the per-tab report fetches
  // below only fire after the user changes a filter, by which point MSAL
  // is ready (the property dropdown only renders once properties arrive).
  // We surface the propertiesQ error in the same banner as report errors.
  useEffect(() => {
    if (propertiesQ.isError) {
      setError(extractErr(propertiesQ.error, 'Failed to load properties.'));
    }
  }, [propertiesQ.isError, propertiesQ.error]);

  const params: ReportParams = useMemo(
    () => ({
      from: filters.from,
      to: filters.to,
      propertyId: filters.propertyId,
    }),
    [filters],
  );

  const fetchActive = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      switch (tabKey) {
        case 'occupancy':
          setOccupancy(await getOccupancyReport(params));
          break;
        case 'revenue':
          setRevenue(await getRevenueReport(params));
          break;
        case 'adr':
          setAdr(await getAdrReport(params));
          break;
        case 'source':
          setSource(await getSourceReport(params));
          break;
      }
    } catch (e) {
      setError(extractErr(e, 'Failed to load report.'));
    } finally {
      setLoading(false);
    }
  }, [tabKey, params]);

  useEffect(() => {
    if (propertiesQ.needsSignIn) return;
    void fetchActive();
  }, [fetchActive]);

  const setTab = (next: TabKey) => {
    router.replace(`/admin/reports?tab=${next}`);
  };

  const onExport = () => {
    const stem = `vrbook-${tabKey}-${filters.from}-${filters.to}`;
    switch (tabKey) {
      case 'occupancy':
        if (!occupancy) return;
        downloadCsv(
          `${stem}.csv`,
          ['date', 'booked_nights', 'available_nights', 'occupancy_pct'],
          occupancy.series.map((p) => [p.date, p.bookedNights, p.availableNights, p.occupancyPct]),
        );
        return;
      case 'revenue':
        if (!revenue) return;
        downloadCsv(
          `${stem}.csv`,
          ['date', 'revenue', 'currency'],
          revenue.series.map((p) => [p.date, p.revenue, p.currency]),
        );
        return;
      case 'adr':
        if (!adr) return;
        downloadCsv(
          `${stem}.csv`,
          ['date', 'adr', 'booked_nights', 'revenue', 'currency'],
          adr.series.map((p) => [p.date, p.adr, p.bookedNights, p.revenue, p.currency]),
        );
        return;
      case 'source':
        if (!source) return;
        downloadCsv(
          `${stem}.csv`,
          ['source', 'bookings', 'nights'],
          source.slices.map((s) => [s.source, s.bookings, s.nights]),
        );
        return;
    }
  };

  if (propertiesQ.needsSignIn) {
    return <SignInGate title="Sign in to view reports" />;
  }

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Reports</h1>
        <p className="text-sm text-muted-foreground">
          Occupancy, revenue, ADR, and source breakdown for the properties you host.
        </p>
      </header>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="flex flex-wrap items-end justify-between gap-3">
        <ReportFilters value={filters} properties={properties} onChange={setFilters} />
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => void fetchActive()}
            disabled={loading}
            className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-2 text-sm hover:bg-accent disabled:opacity-50"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Reload
          </button>
          <button
            type="button"
            onClick={onExport}
            disabled={loading}
            className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-2 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            <Download className="h-4 w-4" /> Export CSV
          </button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {TABS.map((t) => {
          const active = t.key === tabKey;
          return (
            <button
              key={t.key}
              type="button"
              onClick={() => setTab(t.key)}
              className={`rounded-full border px-3 py-1 text-xs ${
                active
                  ? 'border-brand-maroon-700 bg-brand-maroon-700 text-white'
                  : 'border-border bg-background text-muted-foreground hover:bg-accent'
              }`}
            >
              {t.label}
            </button>
          );
        })}
      </div>

      <section>
        {tabKey === 'occupancy' && occupancy && <OccupancyChart report={occupancy} />}
        {tabKey === 'revenue' && revenue && <RevenueChart report={revenue} />}
        {tabKey === 'adr' && adr && <AdrChart report={adr} />}
        {tabKey === 'source' && source && <SourceChart report={source} />}
        {loading && (
          <p className="py-12 text-center text-sm text-muted-foreground">Loading…</p>
        )}
      </section>
    </div>
  );
};

const AdminReportsPage = () => (
  <Suspense fallback={<p className="text-sm text-muted-foreground">Loading…</p>}>
    <AdminReportsBody />
  </Suspense>
);

export default AdminReportsPage;
