'use client';

import { useQueryClient } from '@tanstack/react-query';

import { Field, Skeleton } from '@/components/ui';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import {
  getPropertyCancellation,
  putPropertyCancellation,
  type CancellationModel,
  type PropertyCancellationSettingsDto,
} from '@/lib/api/settings';
import { RecentChangesPanel } from '../RecentChangesPanel';
import { SafeDefault } from '../SafeDefault';
import { SaveBar } from '../SaveBar';
import { SettingsSection } from '../SettingsSection';
import { useSettingsForm } from '../useSettingsForm';

/**
 * VRB-210 worked example — the per-property cancellation-model panel (tenant
 * scope). A single editable field (Tiered vs RefundableUpgrade) driven end-to-end
 * through the shell framework: load → edit → validate → save → audit line. Upgrade
 * price is platform-set (read-only here). Wired to Agent 2's VRB-215 API shape;
 * degrades gracefully until that endpoint lands.
 */
export const CancellationForm = ({
  propertyId,
  initial,
  onSaved,
}: {
  readonly propertyId: string;
  readonly initial: PropertyCancellationSettingsDto;
  readonly onSaved?: () => void;
}) => {
  const tiers = initial.resolvedTiers;
  const form = useSettingsForm<{ model: CancellationModel }>({
    initial: { model: initial.model },
    onSave: (v) => putPropertyCancellation(propertyId, { model: v.model }),
    onSaved,
  });

  return (
    <div className="space-y-6">
      <SettingsSection
        title="Cancellation policy"
        description="How refunds work when a guest cancels this listing."
      >
        <Field label="Policy model" error={form.errors.model}>
          <select
            value={form.values.model}
            onChange={(e) => form.setValue('model', e.target.value as CancellationModel)}
            aria-invalid={form.errors.model ? true : undefined}
            className="min-h-11 w-full rounded-md border border-input bg-background px-3 text-sm aria-[invalid=true]:border-destructive"
          >
            <option value="Tiered">Tiered (graduated refund)</option>
            <option value="RefundableUpgrade">
              Refundable upgrade (+{tiers.upgradePricePct}% of subtotal)
            </option>
          </select>
        </Field>
        <SafeDefault>Tiered</SafeDefault>

        <div className="rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
          <p className="font-medium text-foreground">Platform tiers (read-only)</p>
          <p>
            Full refund up to {tiers.firstTierDays} days before check-in; {tiers.middleTierRefundPct}%
            up to {tiers.secondTierDays} days; no refund within {tiers.finalCutoffHours} hours.
          </p>
          {initial.lastChangedBy && initial.lastChangedAt && (
            <p className="mt-1">
              Last changed by {initial.lastChangedBy} at {new Date(initial.lastChangedAt).toLocaleString()}
            </p>
          )}
        </div>
      </SettingsSection>

      <SaveBar
        isDirty={form.isDirty}
        errorCount={form.errorCount}
        isSaving={form.isSaving}
        savedAt={form.savedAt}
        onSave={form.save}
        onDiscard={form.discard}
      />

      <RecentChangesPanel section="cancellation" propertyId={propertyId} />
    </div>
  );
};

export const CancellationPanel = ({ propertyId }: { readonly propertyId: string }) => {
  const qc = useQueryClient();
  const q = useAuthedQuery<PropertyCancellationSettingsDto>({
    queryKey: ['settings', 'cancellation', propertyId],
    queryFn: () => getPropertyCancellation(propertyId),
  });

  if (q.isLoading) {
    return <Skeleton className="h-40 w-full" />;
  }
  if (q.needsSignIn) {
    return <p className="text-sm text-muted-foreground">Sign in as a tenant admin to manage this setting.</p>;
  }
  if (!q.data) {
    return (
      <p className="text-sm text-muted-foreground">
        Cancellation settings are not available for this listing yet.
      </p>
    );
  }

  return (
    <CancellationForm
      propertyId={propertyId}
      initial={q.data}
      onSaved={() => {
        void qc.invalidateQueries({ queryKey: ['settings', 'changes', 'cancellation', propertyId] });
        void qc.invalidateQueries({ queryKey: ['settings', 'cancellation', propertyId] });
      }}
    />
  );
};
