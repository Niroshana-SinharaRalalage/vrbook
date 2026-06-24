'use client';

import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { type AdrReport } from '@/lib/api/reports';
import { formatCurrency } from '@/lib/utils/currency';

interface AdrChartProps {
  readonly report: AdrReport;
}

const AdrChart = ({ report }: AdrChartProps) => {
  const currency = report.summary.currency;
  const data = report.series.map((p) => ({ date: p.date, adr: p.adr }));

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-3 gap-3 text-sm">
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Average ADR</div>
          <div className="text-lg font-semibold">
            {report.summary.averageAdr == null
              ? '—'
              : formatCurrency(report.summary.averageAdr, currency)}
          </div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total booked nights</div>
          <div className="text-lg font-semibold">{report.summary.totalBookedNights}</div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total revenue</div>
          <div className="text-lg font-semibold">
            {formatCurrency(report.summary.totalRevenue, currency)}
          </div>
        </div>
      </div>

      <div className="h-80 w-full rounded-md border border-border bg-card p-3">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 10, right: 20, bottom: 10, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
            <XAxis dataKey="date" tick={{ fontSize: 11 }} />
            <YAxis tick={{ fontSize: 11 }} />
            <Tooltip
              formatter={(v) => {
                if (v == null || Array.isArray(v)) return '—';
                const n = Number(v);
                return Number.isFinite(n) ? formatCurrency(n, currency) : '—';
              }}
            />
            <Line
              type="monotone"
              dataKey="adr"
              stroke="#7c2d12"
              strokeWidth={2}
              dot={false}
              connectNulls={false}
              name="ADR"
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
};

export default AdrChart;
