'use client';

import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { ConfirmActionModal, Skeleton } from '@/components/ui';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import {
  getPropertyCancellation,
  putPropertyCancellation,
  type CancellationModel,
  type GlobalCancellationTiersDto,
  type PropertyCancellationSettingsDto,
} from '@/lib/api/settings';
import { SafeDefault } from '../SafeDefault';
import { SaveBar } from '../SaveBar';
import { SettingsSection } from '../SettingsSection';
import { useSettingsForm } from '../useSettingsForm';

/**
 * VRB-215 — per-property cancellation-policy panel. The host picks one of the two
 * owner-locked models (Tiered vs Refundable-rate upgrade); there is NO price
 * field — the upgrade price is the platform-set formula, shown read-only. Tier
 * thresholds are platform-global (VRB-216), also read-only. Switching models
 * prompts a confirm (it changes guest-facing refund terms for future bookings).
 */

/** Guest-facing refund terms for a model — what the listing shows the guest. */
const guestPolicyText = (model: CancellationModel, tiers: GlobalCancellationTiersDto): string =>
  model === 'Tiered'
    ? `Guests get a full refund when they cancel ${tiers.firstTierDays}+ days before check-in, ` +
      `${tiers.middleTierRefundPct}% up to ${tiers.secondTierDays} days before, and no refund within ` +
      `${tiers.finalCutoffHours} hours of check-in.`
    : `Guests may pay an extra ${tiers.upgradePricePct}% of the subtotal at booking for a full refund ` +
      `if they cancel before check-in. Without the upgrade, the booking is non-refundable.`;

const MODELS: readonly { value: CancellationModel; label: string }[] = [
  { value: 'Tiered', label: 'Tiered refund' },
  { value: 'RefundableUpgrade', label: 'Refundable-rate upgrade' },
];

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
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const form = useSettingsForm<{ model: CancellationModel }>({
    initial: { model: initial.model },
    onSave: (v) => putPropertyCancellation(propertyId, { model: v.model }),
    onSaved,
  });

  const confirmSave = async () => {
    setSaveError(null);
    const ok = await form.save();
    if (ok) setConfirmOpen(false);
    else setSaveError('Could not save the policy. Please try again.');
  };

  return (
    <div className="space-y-6">
      <SettingsSection
        title="Cancellation policy"
        description="Choose how refunds work when a guest cancels this listing."
      >
        <fieldset>
          <legend className="text-sm font-medium">Policy model</legend>
          <div role="radiogroup" aria-label="Cancellation model" className="mt-2 space-y-3">
            {MODELS.map((m) => {
              const descId = `model-${m.value}-desc`;
              return (
                <label
                  key={m.value}
                  className="flex items-start gap-3 rounded-lg border border-border p-3 has-[:checked]:border-brand-maroon-600"
                >
                  <input
                    type="radio"
                    name="cancellation-model"
                    value={m.value}
                    checked={form.values.model === m.value}
                    onChange={() => form.setValue('model', m.value)}
                    aria-describedby={descId}
                    className="mt-1 h-4 w-4"
                  />
                  <span>
                    <span className="text-sm font-medium">{m.label}</span>
                    <span id={descId} className="mt-0.5 block text-xs text-muted-foreground">
                      {guestPolicyText(m.value, tiers)}
                    </span>
                  </span>
                </label>
              );
            })}
          </div>
        </fieldset>
        <SafeDefault>Tiered refund</SafeDefault>

        <div className="rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
          <p className="font-medium text-foreground">Guests will see</p>
          <p>{guestPolicyText(form.values.model, tiers)}</p>
          {form.values.model === 'Tiered' && (
            <p className="mt-1">Tier thresholds are set platform-wide and are read-only here.</p>
          )}
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
        onSave={async () => {
          setConfirmOpen(true);
          return true; // the confirm modal performs the actual save
        }}
        onDiscard={form.discard}
      />

      <ConfirmActionModal
        open={confirmOpen}
        title="Change cancellation policy?"
        description="This changes the refund terms guests see for future bookings on this listing. Existing bookings keep the policy they were made under."
        confirmLabel="Save policy"
        busyLabel="Saving…"
        busy={form.isSaving}
        error={saveError}
        onCancel={() => {
          setConfirmOpen(false);
          setSaveError(null);
        }}
        onConfirm={confirmSave}
      />
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
    return <Skeleton className="h-48 w-full" />;
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
