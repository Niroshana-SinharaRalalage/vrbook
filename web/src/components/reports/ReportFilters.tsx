'use client';

import { type AdminPropertySummary } from '@/lib/api/catalog';

export interface ReportFiltersValue {
  readonly from: string;
  readonly to: string;
  readonly propertyId: string | null;
}

interface ReportFiltersProps {
  readonly value: ReportFiltersValue;
  readonly properties: readonly AdminPropertySummary[];
  readonly onChange: (next: ReportFiltersValue) => void;
}

const ReportFilters = ({ value, properties, onChange }: ReportFiltersProps) => {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="block text-sm">
        <span className="text-muted-foreground">From</span>
        <input
          type="date"
          value={value.from}
          onChange={(e) => onChange({ ...value, from: e.target.value })}
          className="mt-1 w-40 rounded-md border border-border bg-background px-3 py-2 text-sm"
        />
      </label>
      <label className="block text-sm">
        <span className="text-muted-foreground">To</span>
        <input
          type="date"
          value={value.to}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
          className="mt-1 w-40 rounded-md border border-border bg-background px-3 py-2 text-sm"
        />
      </label>
      <label className="block text-sm">
        <span className="text-muted-foreground">Property</span>
        <select
          value={value.propertyId ?? ''}
          onChange={(e) => onChange({ ...value, propertyId: e.target.value || null })}
          className="mt-1 w-64 rounded-md border border-border bg-background px-3 py-2 text-sm"
        >
          <option value="">All my properties</option>
          {properties.map((p) => (
            <option key={p.id} value={p.id}>
              {p.title}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
};

export default ReportFilters;
