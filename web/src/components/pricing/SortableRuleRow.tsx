'use client';

import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { GripVertical, Pencil, Trash2 } from 'lucide-react';
import { type PricingRule } from '@/lib/api/pricing';

interface SortableRuleRowProps {
  readonly rule: PricingRule;
  readonly currency: string;
  readonly busy: boolean;
  readonly onToggleEnabled: (isEnabled: boolean) => void;
  readonly onEdit: () => void;
  readonly onDelete: () => void;
}

const formatWindow = (rule: PricingRule, _currency: string): string => {
  switch (rule.kind) {
    case 'DateRangeOverride':
      return `${rule.startDate ?? '?'} → ${rule.endDate ?? '?'}`;
    case 'LastMinute':
      return `≤ ${rule.daysBeforeCheckin ?? '?'} days out`;
    case 'LengthOfStay': {
      const min = rule.minNights ?? '?';
      const max = rule.maxNights == null ? '∞' : rule.maxNights;
      return `${min}–${max} nights`;
    }
    default:
      return rule.kind;
  }
};

const formatAdjustment = (rule: PricingRule, currency: string): string => {
  const v = rule.adjustmentValue;
  switch (rule.adjustmentKind) {
    case 'Multiplier':
      return `× ${v}`;
    case 'Absolute':
      return `${v >= 0 ? '+' : ''}${v} ${currency}`;
    case 'Override':
      return `= ${v} ${currency}`;
  }
};

const SortableRuleRow = ({
  rule,
  currency,
  busy,
  onToggleEnabled,
  onEdit,
  onDelete,
}: SortableRuleRowProps) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: rule.id,
  });
  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <tr
      ref={setNodeRef}
      style={style}
      className="border-b border-border last:border-b-0"
    >
      <td className="px-2 py-3">
        <button
          type="button"
          {...attributes}
          {...listeners}
          className="cursor-grab text-muted-foreground hover:text-foreground active:cursor-grabbing"
          aria-label="Drag to reorder"
        >
          <GripVertical className="h-4 w-4" />
        </button>
      </td>
      <td className="px-2 py-3 text-sm tabular-nums">{rule.priority}</td>
      <td className="px-2 py-3 text-sm">{rule.kind}</td>
      <td className="px-2 py-3 text-sm">{formatWindow(rule, currency)}</td>
      <td className="px-2 py-3 text-sm tabular-nums">{formatAdjustment(rule, currency)}</td>
      <td className="px-2 py-3">
        <label className="inline-flex cursor-pointer items-center gap-2 text-xs">
          <input
            type="checkbox"
            checked={rule.isEnabled}
            disabled={busy}
            onChange={(e) => onToggleEnabled(e.target.checked)}
          />
          {rule.isEnabled ? 'On' : 'Off'}
        </label>
      </td>
      <td className="px-2 py-3 text-right">
        <button
          type="button"
          onClick={onEdit}
          disabled={busy}
          className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
        >
          <Pencil className="h-3 w-3" /> Edit
        </button>
        <button
          type="button"
          onClick={onDelete}
          disabled={busy}
          className="ml-1 inline-flex items-center gap-1 rounded border border-destructive/40 px-2 py-1 text-xs text-destructive hover:bg-destructive/10 disabled:opacity-50"
        >
          <Trash2 className="h-3 w-3" /> Delete
        </button>
      </td>
    </tr>
  );
};

export default SortableRuleRow;
