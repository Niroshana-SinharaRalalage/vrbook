'use client';

import { useEffect, useState } from 'react';
import { Award, Sparkles } from 'lucide-react';
import { getMyLoyalty, type LoyaltyAccount, type LoyaltyTier } from '@/lib/api/loyalty';
import { ApiProblemError } from '@/lib/api/client';

const TIER_STYLES: Record<LoyaltyTier, { className: string; label: string }> = {
  Bronze: { className: 'bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200', label: 'Bronze' },
  Silver: { className: 'bg-slate-200 text-slate-900 dark:bg-slate-800 dark:text-slate-100', label: 'Silver' },
  Gold:   { className: 'bg-yellow-200 text-yellow-900 dark:bg-yellow-900 dark:text-yellow-50', label: 'Gold' },
};

const LoyaltyPage = () => {
  const [data, setData] = useState<LoyaltyAccount | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setData(await getMyLoyalty());
      } catch (err) {
        if (err instanceof ApiProblemError && err.status === 404) {
          // No account yet — show the "complete a stay" empty state below.
          setData(null);
        } else {
          setError(err instanceof Error ? err.message : 'Failed to load loyalty status.');
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  if (loading) {
    return <p className="p-6 text-sm text-muted-foreground">Loading…</p>;
  }
  if (error) {
    return <p className="p-6 text-sm text-destructive">{error}</p>;
  }
  if (!data) {
    return (
      <div className="mx-auto max-w-xl space-y-3 p-6">
        <h1 className="text-2xl font-semibold tracking-tight">Loyalty</h1>
        <p className="text-sm text-muted-foreground">
          Your loyalty account opens automatically on your first completed stay. After
          checkout the host has up to 24 hours to wrap up the booking; once it lands as
          Completed your tier shows here.
        </p>
      </div>
    );
  }

  const style = TIER_STYLES[data.tier];

  return (
    <div className="mx-auto max-w-xl space-y-6 p-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Loyalty</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Your tier is computed from completed stays. Discounts apply automatically to your next quote.
        </p>
      </header>

      <section className="rounded-lg border border-border bg-card p-6">
        <div className="flex items-center gap-3">
          <span className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-sm font-semibold ${style.className}`}>
            <Award className="h-4 w-4" />
            {style.label}
          </span>
          <span className="text-sm text-muted-foreground">
            {data.completedStayCount} {data.completedStayCount === 1 ? 'completed stay' : 'completed stays'}
          </span>
        </div>

        <dl className="mt-4 grid grid-cols-2 gap-3 text-sm">
          <div>
            <dt className="text-muted-foreground">Current discount</dt>
            <dd className="mt-0.5 text-lg font-semibold">{data.currentDiscountPct}%</dd>
          </div>
          {data.nextTier && data.staysUntilNextTier !== null && (
            <div>
              <dt className="text-muted-foreground">Next tier</dt>
              <dd className="mt-0.5 flex items-center gap-1 text-lg font-semibold">
                <Sparkles className="h-4 w-4 text-brand-maroon-700" />
                {data.nextTier} · {data.staysUntilNextTier} {data.staysUntilNextTier === 1 ? 'stay' : 'stays'} to go
              </dd>
            </div>
          )}
          {!data.nextTier && (
            <div>
              <dt className="text-muted-foreground">Status</dt>
              <dd className="mt-0.5 text-lg font-semibold">Top tier — thanks for staying!</dd>
            </div>
          )}
        </dl>
      </section>

      <p className="text-xs text-muted-foreground">
        Phase 1 tiers: Bronze (1+ stay, 0% discount), Silver (3+ stays, 5%), Gold (6+ stays, 10%).
        Discount applies on quote-compute for your next booking.
      </p>
    </div>
  );
};

export default LoyaltyPage;
