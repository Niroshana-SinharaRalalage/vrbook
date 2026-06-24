import { apiFetch } from './client';

// Mirror VrBook.Contracts.Common.Money / DateRange.
export interface Money {
  readonly amount: number;
  readonly currency: string;
}

export interface DateRange {
  readonly start: string;
  readonly end: string;
}

export interface NightlyLine {
  readonly date: string;
  readonly amount: Money;
  readonly ruleApplied: string | null;
}

export interface FeeLine {
  readonly label: string;
  readonly kind: string;
  readonly amount: Money;
}

export interface Quote {
  readonly propertyId: string;
  readonly range: DateRange;
  readonly guests: number;
  readonly nightly: readonly NightlyLine[];
  readonly fees: readonly FeeLine[];
  readonly subtotal: Money;
  readonly discount: Money;
  readonly taxes: Money;
  readonly total: Money;
  readonly expiresAt: string;
}

export interface QuoteRequestBody {
  readonly checkin: string;       // YYYY-MM-DD (DateOnly)
  readonly checkout: string;      // YYYY-MM-DD
  readonly guests: number;
  readonly applyLoyaltyDiscount?: boolean;
}

export const computeQuote = (propertyId: string, body: QuoteRequestBody): Promise<Quote> =>
  apiFetch<Quote>(`/api/v1/properties/${encodeURIComponent(propertyId)}/quotes`, {
    body: { applyLoyaltyDiscount: false, ...body },
    anonymous: true,
  });

// Slice 6: mirror VrBook.Contracts.Enums.PricingRuleKind / PricingAdjustmentKind.
export type PricingRuleKind =
  | 'DateRangeOverride'
  | 'LastMinute'
  | 'LengthOfStay'
  | 'DayOfWeek'
  | 'Base';

export type PricingAdjustmentKind = 'Absolute' | 'Multiplier' | 'Override';

export interface PricingRule {
  readonly id: string;
  readonly kind: PricingRuleKind;
  readonly priority: number;
  readonly startDate: string | null;
  readonly endDate: string | null;
  readonly dayOfWeekMask: number | null;
  readonly minNights: number | null;
  readonly maxNights: number | null;
  readonly daysBeforeCheckin: number | null;
  readonly adjustmentKind: PricingAdjustmentKind;
  readonly adjustmentValue: number;
  readonly isEnabled: boolean;
}

export interface CreatePricingRuleRequest {
  readonly kind: PricingRuleKind;
  readonly priority: number;
  readonly startDate: string | null;
  readonly endDate: string | null;
  readonly dayOfWeekMask: number | null;
  readonly minNights: number | null;
  readonly maxNights: number | null;
  readonly daysBeforeCheckin: number | null;
  readonly adjustmentKind: PricingAdjustmentKind;
  readonly adjustmentValue: number;
  readonly isEnabled: boolean;
}

export interface PricingPlan {
  readonly id: string;
  readonly propertyId: string;
  readonly baseNightlyRate: number;
  readonly weekendRate: number;
  readonly currency: string;
  readonly minStayNights: number;
  readonly maxStayNights: number;
  readonly dynamicEnabled: boolean;
  readonly rules: readonly PricingRule[];
  readonly fees: readonly {
    readonly id: string;
    readonly kind: string;
    readonly amount: number;
    readonly basis: string;
    readonly freeThreshold: number | null;
    readonly label: string;
  }[];
}

export const getPricingPlan = (propertyId: string): Promise<PricingPlan> =>
  apiFetch<PricingPlan>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing`);

// --- Slice 6 rule CRUD + reorder ----------------------------------------

export const createPricingRule = (propertyId: string, body: CreatePricingRuleRequest): Promise<PricingRule> =>
  apiFetch<PricingRule>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing/rules`, {
    method: 'POST',
    body,
  });

export const updatePricingRule = (propertyId: string, ruleId: string, body: CreatePricingRuleRequest): Promise<PricingRule> =>
  apiFetch<PricingRule>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing/rules/${encodeURIComponent(ruleId)}`, {
    method: 'PUT',
    body,
  });

export const deletePricingRule = (propertyId: string, ruleId: string): Promise<void> =>
  apiFetch<void>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing/rules/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE',
  });

export const setPricingRuleEnabled = (propertyId: string, ruleId: string, isEnabled: boolean): Promise<PricingRule> =>
  apiFetch<PricingRule>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing/rules/${encodeURIComponent(ruleId)}/enabled`, {
    method: 'PATCH',
    body: { isEnabled },
  });

export const reorderPricingRules = (propertyId: string, ruleIds: readonly string[]): Promise<PricingPlan> =>
  apiFetch<PricingPlan>(`/api/v1/properties/${encodeURIComponent(propertyId)}/pricing/rules/reorder`, {
    method: 'POST',
    body: { ruleIds },
  });
