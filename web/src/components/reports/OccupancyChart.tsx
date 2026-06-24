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
import { type OccupancyReport } from '@/lib/api/reports';

interface OccupancyChartProps {
  readonly report: OccupancyReport;
}

const OccupancyChart = ({ report }: OccupancyChartProps) => {
  const data = report.series.map((p) => ({
    date: p.date,
    occupancy: p.occupancyPct,
    booked: p.bookedNights,
    available: p.availableNights,
  }));

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-3 gap-3 text-sm">
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Avg occupancy</div>
          <div className="text-lg font-semibold">
            {report.summary.averageOccupancyPct == null
              ? '—'
              : `${report.summary.averageOccupancyPct.toFixed(2)}%`}
          </div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total booked nights</div>
          <div className="text-lg font-semibold">{report.summary.totalBookedNights}</div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total available nights</div>
          <div className="text-lg font-semibold">{report.summary.totalAvailableNights}</div>
        </div>
      </div>

      <div className="h-80 w-full rounded-md border border-border bg-card p-3">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 10, right: 20, bottom: 10, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
            <XAxis dataKey="date" tick={{ fontSize: 11 }} />
            <YAxis
              tick={{ fontSize: 11 }}
              tickFormatter={(v: number) => `${v}%`}
              domain={[0, 100]}
            />
            <Tooltip
              formatter={(v) => {
                if (v == null || Array.isArray(v)) return '—';
                const n = Number(v);
                return Number.isFinite(n) ? `${n.toFixed(2)}%` : '—';
              }}
            />
            <Line
              type="monotone"
              dataKey="occupancy"
              stroke="#7c2d12"
              strokeWidth={2}
              dot={false}
              connectNulls={false}
              name="Occupancy"
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
};

export default OccupancyChart;
