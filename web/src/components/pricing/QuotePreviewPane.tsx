'use client';

import { useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { computeQuote, type Quote } from '@/lib/api/pricing';
import { formatCurrency } from '@/lib/utils/currency';

interface QuotePreviewPaneProps {
  readonly propertyId: string;
}

const defaultCheckin = (): string => {
  const d = new Date();
  d.setDate(d.getDate() + ((5 - d.getDay() + 7) % 7 || 7)); // next Friday
  return d.toISOString().slice(0, 10);
};

const defaultCheckout = (checkin: string): string => {
  const d = new Date(checkin);
  d.setDate(d.getDate() + 7);
  return d.toISOString().slice(0, 10);
};

const QuotePreviewPane = ({ propertyId }: QuotePreviewPaneProps) => {
  const initialCheckin = defaultCheckin();
  const [checkin, setCheckin] = useState(initialCheckin);
  const [checkout, setCheckout] = useState(defaultCheckout(initialCheckin));
  const [guests, setGuests] = useState(2);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const refresh = async () => {
    setBusy(true);
    setErr(null);
    try {
      const q = await computeQuote(propertyId, { checkin, checkout, guests });
      setQuote(q);
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to compute quote.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside className="space-y-3 rounded-xl border border-border bg-card p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">Quote preview</h3>
        <button
          type="button"
          onClick={() => void refresh()}
          disabled={busy}
          className="inline-flex items-center gap-1 rounded-md border border-border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
        >
          <RefreshCw className={`h-3 w-3 ${busy ? 'animate-spin' : ''}`} /> Refresh
        </button>
      </div>

      <div className="grid grid-cols-2 gap-2 text-xs">
        <label>
          <span className="text-muted-foreground">Check-in</span>
          <input
            type="date"
            value={checkin}
            onChange={(e) => setCheckin(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-xs"
          />
        </label>
        <label>
          <span className="text-muted-foreground">Check-out</span>
          <input
            type="date"
            value={checkout}
            onChange={(e) => setCheckout(e.target.value)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-xs"
          />
        </label>
        <label className="col-span-2">
          <span className="text-muted-foreground">Guests</span>
          <input
            type="number"
            min={1}
            value={guests}
            onChange={(e) => setGuests(Number(e.target.value) || 1)}
            className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-xs"
          />
        </label>
      </div>

      {err && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
          {err}
        </div>
      )}

      {quote && (
        <div className="space-y-2 text-xs">
          <div className="overflow-x-auto rounded-md border border-border">
            <table className="w-full">
              <thead className="bg-muted/40 text-muted-foreground">
                <tr>
                  <th className="px-2 py-1 text-left">Date</th>
                  <th className="px-2 py-1 text-right">Amount</th>
                  <th className="px-2 py-1 text-right">Rule</th>
                </tr>
              </thead>
              <tbody>
                {quote.nightly.map((n) => (
                  <tr key={n.date} className="border-t border-border">
                    <td className="px-2 py-1">{n.date}</td>
                    <td className="px-2 py-1 text-right tabular-nums">
                      {formatCurrency(n.amount.amount, n.amount.currency)}
                    </td>
                    <td className="px-2 py-1 text-right">
                      {n.ruleApplied && (
                        <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] text-amber-900 dark:bg-amber-900/40 dark:text-amber-200">
                          {n.ruleApplied}
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <dl className="space-y-1">
            <div className="flex justify-between">
              <dt className="text-muted-foreground">Subtotal</dt>
              <dd className="tabular-nums">{formatCurrency(quote.subtotal.amount, quote.subtotal.currency)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-muted-foreground">Taxes</dt>
              <dd className="tabular-nums">{formatCurrency(quote.taxes.amount, quote.taxes.currency)}</dd>
            </div>
            <div className="flex justify-between border-t border-border pt-1 font-medium">
              <dt>Total</dt>
              <dd className="tabular-nums">{formatCurrency(quote.total.amount, quote.total.currency)}</dd>
            </div>
          </dl>
        </div>
      )}

      {!quote && !err && (
        <p className="text-xs text-muted-foreground">
          Pick a range and click Refresh to see how the rules affect a quote.
        </p>
      )}
    </aside>
  );
};

export default QuotePreviewPane;
