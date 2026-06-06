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

export interface PricingPlan {
  readonly id: string;
  readonly propertyId: string;
  readonly baseNightlyRate: number;
  readonly weekendRate: number;
  readonly currency: string;
  readonly minStayNights: number;
  readonly maxStayNights: number;
  readonly dynamicEnabled: boolean;
  readonly rules: readonly unknown[];
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
