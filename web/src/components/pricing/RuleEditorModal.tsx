'use client';

import { useState } from 'react';
import { X } from 'lucide-react';
import {
  type CreatePricingRuleRequest,
  type PricingAdjustmentKind,
  type PricingRule,
  type PricingRuleKind,
} from '@/lib/api/pricing';

interface RuleEditorModalProps {
  readonly rule: PricingRule | null; // null = creating
  readonly currency: string;
  readonly onCancel: () => void;
  readonly onSave: (body: CreatePricingRuleRequest) => Promise<void>;
}

const KIND_LABELS: Record<PricingRuleKind, string> = {
  DateRangeOverride: 'Seasonal (date range)',
  LastMinute: 'Last-minute',
  LengthOfStay: 'Length of stay',
  DayOfWeek: 'Day of week (not handled)',
  Base: 'Base (not handled)',
};

const HANDLED_KINDS: readonly PricingRuleKind[] = [
  'DateRangeOverride',
  'LastMinute',
  'LengthOfStay',
];

const ADJUSTMENT_LABELS: Record<PricingAdjustmentKind, string> = {
  Absolute: 'Absolute (+/- per night)',
  Multiplier: 'Multiplier (× nightly rate)',
  Override: 'Override (replace rate)',
};

const RuleEditorModal = ({ rule, currency, onCancel, onSave }: RuleEditorModalProps) => {
  const [kind, setKind] = useState<PricingRuleKind>(rule?.kind ?? 'DateRangeOverride');
  const [startDate, setStartDate] = useState(rule?.startDate ?? '');
  const [endDate, setEndDate] = useState(rule?.endDate ?? '');
  const [minNights, setMinNights] = useState<number | ''>(rule?.minNights ?? '');
  const [maxNights, setMaxNights] = useState<number | ''>(rule?.maxNights ?? '');
  const [daysBeforeCheckin, setDaysBeforeCheckin] = useState<number | ''>(rule?.daysBeforeCheckin ?? '');
  const [adjustmentKind, setAdjustmentKind] = useState<PricingAdjustmentKind>(
    rule?.adjustmentKind ?? 'Multiplier',
  );
  const [adjustmentValue, setAdjustmentValue] = useState<number | ''>(rule?.adjustmentValue ?? 1.5);
  const [isEnabled, setIsEnabled] = useState(rule?.isEnabled ?? true);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  // §2.4.1: LastMinute × Override and LengthOfStay × Override are rejected.
  // Hide that option in the UI so the owner can't pick the rejected combo.
  const overrideAllowed = kind === 'DateRangeOverride';

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!overrideAllowed && adjustmentKind === 'Override') {
      setErr(`${kind} cannot use Override (see SLICE6_PLAN §2.4.1).`);
      return;
    }
    setBusy(true);
    setErr(null);
    try {
      const body: CreatePricingRuleRequest = {
        kind,
        priority: rule?.priority ?? 0, // server may overwrite when null is passed; we keep current on edit
        startDate: kind === 'DateRangeOverride' ? startDate || null : null,
        endDate: kind === 'DateRangeOverride' ? endDate || null : null,
        dayOfWeekMask: null,
        minNights: kind === 'LengthOfStay' && minNights !== '' ? Number(minNights) : null,
        maxNights: kind === 'LengthOfStay' && maxNights !== '' ? Number(maxNights) : null,
        daysBeforeCheckin: kind === 'LastMinute' && daysBeforeCheckin !== '' ? Number(daysBeforeCheckin) : null,
        adjustmentKind,
        adjustmentValue: adjustmentValue === '' ? 0 : Number(adjustmentValue),
        isEnabled,
      };
      await onSave(body);
    } catch (e2) {
      setErr(e2 instanceof Error ? e2.message : 'Save failed.');
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
          <h3 className="text-base font-medium">
            {rule ? 'Edit pricing rule' : 'Add pricing rule'}
          </h3>
          <button type="button" onClick={onCancel} className="rounded-md p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>

        <label className="block text-sm">
          <span className="text-muted-foreground">Rule kind</span>
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as PricingRuleKind)}
            disabled={!!rule /* don't allow kind change on existing rule; delete + recreate instead */}
            className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
          >
            {HANDLED_KINDS.map((k) => (
              <option key={k} value={k}>
                {KIND_LABELS[k]}
              </option>
            ))}
          </select>
        </label>

        {kind === 'DateRangeOverride' && (
          <div className="grid grid-cols-2 gap-3">
            <label className="block text-sm">
              <span className="text-muted-foreground">Start date</span>
              <input
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                required
                className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              />
            </label>
            <label className="block text-sm">
              <span className="text-muted-foreground">End date</span>
              <input
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                required
                className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              />
            </label>
          </div>
        )}

        {kind === 'LastMinute' && (
          <label className="block text-sm">
            <span className="text-muted-foreground">Days before check-in</span>
            <input
              type="number"
              min={1}
              max={365}
              value={daysBeforeCheckin}
              onChange={(e) => setDaysBeforeCheckin(e.target.value === '' ? '' : Number(e.target.value))}
              required
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
            />
          </label>
        )}

        {kind === 'LengthOfStay' && (
          <div className="grid grid-cols-2 gap-3">
            <label className="block text-sm">
              <span className="text-muted-foreground">Min nights</span>
              <input
                type="number"
                min={1}
                value={minNights}
                onChange={(e) => setMinNights(e.target.value === '' ? '' : Number(e.target.value))}
                required
                className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              />
            </label>
            <label className="block text-sm">
              <span className="text-muted-foreground">Max nights (optional)</span>
              <input
                type="number"
                min={1}
                value={maxNights}
                onChange={(e) => setMaxNights(e.target.value === '' ? '' : Number(e.target.value))}
                className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              />
            </label>
          </div>
        )}

        <div className="grid grid-cols-2 gap-3">
          <label className="block text-sm">
            <span className="text-muted-foreground">Adjustment kind</span>
            <select
              value={adjustmentKind}
              onChange={(e) => setAdjustmentKind(e.target.value as PricingAdjustmentKind)}
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
            >
              <option value="Absolute">{ADJUSTMENT_LABELS.Absolute}</option>
              <option value="Multiplier">{ADJUSTMENT_LABELS.Multiplier}</option>
              {overrideAllowed && <option value="Override">{ADJUSTMENT_LABELS.Override}</option>}
            </select>
          </label>
          <label className="block text-sm">
            <span className="text-muted-foreground">
              Value {adjustmentKind === 'Multiplier' ? '(e.g. 1.5 = +50%)' : `(${currency})`}
            </span>
            <input
              type="number"
              step="0.01"
              value={adjustmentValue}
              onChange={(e) => setAdjustmentValue(e.target.value === '' ? '' : Number(e.target.value))}
              required
              className="mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
            />
          </label>
        </div>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={isEnabled}
            onChange={(e) => setIsEnabled(e.target.checked)}
            className="rounded"
          />
          <span>Enabled (applies to new quotes immediately)</span>
        </label>

        {err && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
            {err}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="rounded-md border border-border px-3 py-1.5 text-sm hover:bg-accent"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy}
            className="rounded-md bg-brand-maroon-700 px-3 py-1.5 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </div>
  );
};

export default RuleEditorModal;
