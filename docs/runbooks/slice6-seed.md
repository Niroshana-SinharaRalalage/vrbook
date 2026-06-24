# Slice 6 dev seed — sample pricing rule

A one-shot SQL snippet to drop a sample Seasonal rule onto a demo
property's pricing plan, for verifying that the quote engine + admin
editor + public quote widget agree.

There is no Program.cs seeder for arbitrary domain rows in this repo
(matching the existing convention — Slice 5 shipped its dev shortcuts
as DevAuth bridge endpoints, not seeders). Run this SQL directly
against staging Postgres when you want the demo data.

## What this seeds

A Seasonal rule on the first PricingPlan found, applying +50% to
nights between 2026-12-20 and 2027-01-05.

```sql
-- Slice 6 demo: Seasonal +50% rule on the first pricing plan.
-- Idempotent: only inserts when no rule of this kind+window exists.
INSERT INTO pricing.pricing_rules (
  "Id", pricing_plan_id, kind, priority,
  start_date, end_date,
  day_of_week_mask, min_nights, max_nights, days_before_checkin,
  adjustment_kind, adjustment_value, is_enabled
)
SELECT
  gen_random_uuid(),
  (SELECT "Id" FROM pricing.pricing_plans ORDER BY created_at LIMIT 1),
  'DateRangeOverride',
  0,
  '2026-12-20'::date,
  '2027-01-05'::date,
  NULL, NULL, NULL, NULL,
  'Multiplier',
  1.5,
  true
WHERE NOT EXISTS (
  SELECT 1 FROM pricing.pricing_rules r
  WHERE r.kind = 'DateRangeOverride'
    AND r.start_date = '2026-12-20'::date
    AND r.end_date = '2027-01-05'::date
);
```

## Verification after seeding

1. Open `/admin/pricing` → property dropdown shows the demo property → 
   the rules table has a `DateRangeOverride` row with priority 0,
   multiplier 1.5, enabled.
2. Click into the Quote Preview pane on the right → date range
   2026-12-22 to 2026-12-26 (4 nights, all in window) → Refresh → 
   per-night badges show `seasonal` → subtotal = `base × 1.5 × 4`.
3. Open `/properties/{demo-slug}` in a second tab → public quote
   widget for the same dates → total matches the preview. Same
   ComputeQuoteHandler, no drift.

## Undo

```sql
DELETE FROM pricing.pricing_rules
WHERE kind = 'DateRangeOverride'
  AND start_date = '2026-12-20'::date
  AND end_date = '2027-01-05'::date;
```
