import { apiFetch } from './client';

export type LoyaltyTier = 'Bronze' | 'Silver' | 'Gold';

export interface LoyaltyAccount {
  readonly userId: string;
  readonly tier: LoyaltyTier;
  readonly completedStayCount: number;
  readonly currentDiscountPct: number;
  readonly nextTier: LoyaltyTier | null;
  readonly staysUntilNextTier: number | null;
}

export const getMyLoyalty = (): Promise<LoyaltyAccount> =>
  apiFetch<LoyaltyAccount>('/api/v1/me/loyalty');
