'use client';

import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { type RevenueReport } from '@/lib/api/reports';
import { formatCurrency } from '@/lib/utils/currency';

interface RevenueChartProps {
  readonly report: RevenueReport;
}

const RevenueChart = ({ report }: RevenueChartProps) => {
  const currency = report.summary.currency;
  const data = report.series.map((p) => ({ date: p.date, revenue: p.revenue }));

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total revenue</div>
          <div className="text-lg font-semibold">
            {formatCurrency(report.summary.totalRevenue, currency)}
          </div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Confirmed bookings</div>
          <div className="text-lg font-semibold">{report.summary.confirmedBookings}</div>
        </div>
      </div>

      <div className="h-80 w-full rounded-md border border-border bg-card p-3">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} margin={{ top: 10, right: 20, bottom: 10, left: 0 }}>
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
            <Bar dataKey="revenue" fill="#7c2d12" name="Revenue" />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
};

export default RevenueChart;
