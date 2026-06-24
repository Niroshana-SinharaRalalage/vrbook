'use client';

import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import { type SourceReport } from '@/lib/api/reports';

interface SourceChartProps {
  readonly report: SourceReport;
}

const COLORS = ['#7c2d12', '#b45309', '#1d4ed8', '#15803d', '#a21caf'];

const SourceChart = ({ report }: SourceChartProps) => {
  // Hide zero-bookings slices so the chart isn't cluttered.
  const slices = report.slices.filter((s) => s.bookings > 0);
  const data = slices.map((s) => ({ name: s.source, value: s.bookings }));

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total bookings</div>
          <div className="text-lg font-semibold">{report.summary.totalBookings}</div>
        </div>
        <div className="rounded-md border border-border bg-card p-3">
          <div className="text-xs text-muted-foreground">Total nights</div>
          <div className="text-lg font-semibold">{report.summary.totalNights}</div>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <div className="h-80 w-full rounded-md border border-border bg-card p-3">
          {data.length === 0 ? (
            <p className="flex h-full items-center justify-center text-sm text-muted-foreground">
              No bookings in this range.
            </p>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Tooltip />
                <Legend />
                <Pie
                  data={data}
                  dataKey="value"
                  nameKey="name"
                  cx="50%"
                  cy="45%"
                  outerRadius={90}
                  label={(p: { name?: string | number; value?: string | number }) =>
                    p.name != null && p.value != null ? `${p.name}: ${p.value}` : ''
                  }
                >
                  {data.map((_entry, i) => (
                    <Cell key={i} fill={COLORS[i % COLORS.length]} />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>

        <div className="rounded-md border border-border bg-card p-3">
          <table className="w-full text-sm">
            <thead className="text-xs text-muted-foreground">
              <tr>
                <th className="px-2 py-1 text-left">Source</th>
                <th className="px-2 py-1 text-right">Bookings</th>
                <th className="px-2 py-1 text-right">Nights</th>
              </tr>
            </thead>
            <tbody>
              {slices.map((s) => (
                <tr key={s.source} className="border-t border-border">
                  <td className="px-2 py-1">{s.source}</td>
                  <td className="px-2 py-1 text-right tabular-nums">{s.bookings}</td>
                  <td className="px-2 py-1 text-right tabular-nums">{s.nights}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

export default SourceChart;
