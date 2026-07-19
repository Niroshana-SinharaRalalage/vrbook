'use client';

import { useQueryClient } from '@tanstack/react-query';

import { Field, Input, Skeleton } from '@/components/ui';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import {
  getGlobalCancellationTiers,
  putGlobalCancellationTiers,
  type GlobalCancellationTiersDto,
  type SetGlobalTiersBody,
} from '@/lib/api/settings';
import { RecentChangesPanel } from '../RecentChangesPanel';
import { SaveBar } from '../SaveBar';
import { SettingsSection } from '../SettingsSection';
import { useSettingsForm, type FieldErrors } from '../useSettingsForm';

/**
 * VRB-216 (web) — platform-admin panel for the global cancellation-tier
 * schedule that every Tiered-model listing inherits (VRB-215 shows these
 * read-only). Client validation mirrors `SetGlobalTiersValidator` 1:1; the
 * server's `422 problem.errors` maps back onto the fields regardless.
 */

// Mirrors SetGlobalTiersValidator (Modules.Admin/Application/Settings/GlobalTiers.cs).
const validate = (v: SetGlobalTiersBody): FieldErrors<SetGlobalTiersBody> => {
  const e: FieldErrors<SetGlobalTiersBody> = {};
  if (!(v.firstTierDays > v.secondTierDays)) {
    e.firstTierDays = 'Must be greater than the partial-refund cutoff (days).';
  }
  if (v.secondTierDays < 0) e.secondTierDays = 'Must be 0 or more.';
  if (v.middleTierRefundPct < 0 || v.middleTierRefundPct > 100) {
    e.middleTierRefundPct = 'Must be between 0 and 100.';
  }
  if (!(v.finalCutoffHours > 0)) e.finalCutoffHours = 'Must be greater than 0.';
  if (v.upgradePricePct < 0 || v.upgradePricePct > 100) {
    e.upgradePricePct = 'Must be between 0 and 100.';
  }
  return e;
};

const FIELDS: readonly {
  key: keyof SetGlobalTiersBody;
  label: string;
  hint: string;
  min: number;
  max?: number;
}[] = [
  { key: 'firstTierDays', label: 'Full-refund cutoff (days before check-in)', hint: 'Cancel this many days ahead → full refund.', min: 1 },
  { key: 'secondTierDays', label: 'Partial-refund cutoff (days before check-in)', hint: 'Between this and the full-refund cutoff → partial refund.', min: 0 },
  { key: 'middleTierRefundPct', label: 'Partial refund (%)', hint: 'Refunded in the partial window.', min: 0, max: 100 },
  { key: 'finalCutoffHours', label: 'No-refund cutoff (hours before check-in)', hint: 'Within this window → no refund.', min: 1 },
  { key: 'upgradePricePct', label: 'Refundable-upgrade price (% of subtotal)', hint: 'Surcharge for the refundable-upgrade model.', min: 0, max: 100 },
];

const toBody = (t: GlobalCancellationTiersDto): SetGlobalTiersBody => ({
  firstTierDays: t.firstTierDays,
  secondTierDays: t.secondTierDays,
  middleTierRefundPct: t.middleTierRefundPct,
  finalCutoffHours: t.finalCutoffHours,
  upgradePricePct: t.upgradePricePct,
});

export const GlobalTiersForm = ({
  initial,
  onSaved,
}: {
  readonly initial: GlobalCancellationTiersDto;
  readonly onSaved?: () => void;
}) => {
  const form = useSettingsForm<SetGlobalTiersBody>({
    initial: toBody(initial),
    validate,
    onSave: (v) => putGlobalCancellationTiers(v),
    onSaved,
  });

  return (
    <div className="space-y-6">
      <SettingsSection
        title="Global cancellation tiers"
        description="The platform-wide refund schedule every Tiered-policy listing inherits. Hosts opt in; they can't edit these numbers."
      >
        <div className="grid gap-4 md:grid-cols-2">
          {FIELDS.map((f) => (
            <Field key={f.key} label={f.label} description={f.hint} error={form.errors[f.key]}>
              <Input
                type="number"
                inputMode="numeric"
                min={f.min}
                max={f.max}
                value={String(form.values[f.key])}
                onChange={(e) => form.setValue(f.key, Number(e.target.value))}
              />
            </Field>
          ))}
        </div>
        {initial.lastChangedBy && initial.lastChangedAt && (
          <p className="text-xs text-muted-foreground">
            Last changed by {initial.lastChangedBy} at {new Date(initial.lastChangedAt).toLocaleString()} (v{initial.version})
          </p>
        )}
      </SettingsSection>

      <SaveBar
        isDirty={form.isDirty}
        errorCount={form.errorCount}
        isSaving={form.isSaving}
        savedAt={form.savedAt}
        onSave={form.save}
        onDiscard={form.discard}
      />

      <RecentChangesPanel section="platform" />
    </div>
  );
};

export const GlobalTiersPanel = () => {
  const qc = useQueryClient();
  const q = useAuthedQuery<GlobalCancellationTiersDto>({
    queryKey: ['settings', 'global-tiers'],
    queryFn: getGlobalCancellationTiers,
  });

  if (q.isLoading) return <Skeleton className="h-64 w-full" />;
  if (q.needsSignIn) {
    return <p className="text-sm text-muted-foreground">Sign in as a platform admin to manage the global tiers.</p>;
  }
  if (!q.data) {
    return <p className="text-sm text-muted-foreground">The global cancellation tiers are not available yet.</p>;
  }

  return (
    <GlobalTiersForm
      initial={q.data}
      onSaved={() => {
        void qc.invalidateQueries({ queryKey: ['settings', 'global-tiers'] });
        void qc.invalidateQueries({ queryKey: ['settings', 'changes', 'platform', null] });
      }}
    />
  );
};
