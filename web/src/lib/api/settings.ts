import { apiFetch } from './client';

/**
 * VRB-210 — TypeScript mirror of the settings API surface (VRB-215/216, built by
 * Agent 2). Routes + DTO shapes are the agreed contract:
 *   tenant   base `/api/v1/admin/settings`            (HasTenantRole "tenant_admin")
 *   platform base `/api/v1/admin/platform/settings`   (PlatformAdmin)
 * The shell (VRB-210) wires only the tenant cancellation-model panel as its
 * worked example; the platform DTOs/fns are defined so the VRB-216 panel stories
 * import them directly. Until Agent 2's endpoints land these calls 404/501 —
 * the UI degrades to its error/empty states.
 */

// ---- Audit trail (VRB-211, SettingsChangeDto — merged in #28) ----
export interface SettingsChangeDto {
  readonly actor: string;
  readonly action: string; // settings.<section>.<verb>
  readonly before: string | null;
  readonly after: string | null;
  readonly at: string; // ISO timestamp
}

export const getSettingsChanges = (
  section: string,
  propertyId?: string,
): Promise<readonly SettingsChangeDto[]> => {
  const qs = new URLSearchParams({ section });
  if (propertyId) qs.set('propertyId', propertyId);
  return apiFetch<readonly SettingsChangeDto[]>(`/api/v1/admin/settings/changes?${qs.toString()}`);
};

// ---- Tenant-admin: per-property cancellation model (worked example) ----
export type CancellationModel = 'Tiered' | 'RefundableUpgrade';

export interface GlobalCancellationTiersDto {
  readonly firstTierDays: number;
  readonly secondTierDays: number;
  readonly middleTierRefundPct: number;
  readonly finalCutoffHours: number;
  readonly upgradePricePct: number;
  readonly version: number;
  readonly lastChangedBy: string | null;
  readonly lastChangedAt: string | null;
}

export interface PropertyCancellationSettingsDto {
  readonly propertyId: string;
  readonly model: CancellationModel;
  readonly resolvedTiers: GlobalCancellationTiersDto;
  readonly lastChangedBy: string | null;
  readonly lastChangedAt: string | null;
}

export const getPropertyCancellation = (propertyId: string): Promise<PropertyCancellationSettingsDto> =>
  apiFetch<PropertyCancellationSettingsDto>(
    `/api/v1/admin/settings/cancellation/${encodeURIComponent(propertyId)}`,
  );

export const putPropertyCancellation = (
  propertyId: string,
  body: { model: CancellationModel },
): Promise<PropertyCancellationSettingsDto> =>
  apiFetch<PropertyCancellationSettingsDto>(
    `/api/v1/admin/settings/cancellation/${encodeURIComponent(propertyId)}`,
    { method: 'PUT', body },
  );

// ---- Platform-admin: DTOs for the VRB-216 panels (defined for reuse) ----
export interface TenantFeeOverrideDto {
  readonly tenantId: string;
  readonly feeBps: number;
}

export interface PlatformFeeConfigDto {
  readonly defaultBps: number;
  readonly overrides: readonly TenantFeeOverrideDto[];
}

export interface TaxPostureDto {
  readonly facilitatorActive: boolean;
  readonly perStateEnabled: Readonly<Record<string, boolean>>;
}

export const getGlobalCancellationTiers = (): Promise<GlobalCancellationTiersDto> =>
  apiFetch<GlobalCancellationTiersDto>('/api/v1/admin/platform/settings/cancellation-tiers');

export const getPlatformFee = (): Promise<PlatformFeeConfigDto> =>
  apiFetch<PlatformFeeConfigDto>('/api/v1/admin/platform/settings/platform-fee');

export const getTaxPosture = (): Promise<TaxPostureDto> =>
  apiFetch<TaxPostureDto>('/api/v1/admin/platform/settings/tax-posture');
