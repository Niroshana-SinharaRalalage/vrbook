# Slice 7 dev seed — reports test data

One-shot SQL to populate the dev DB with a mix of confirmed bookings,
external reservations, and an availability block so the four report
tabs render with non-trivial data.

This mirrors the Slice 6 pattern (`slice6-seed.md`) — a runbook SQL
snippet rather than a `Program.cs` seeder, matching the existing
convention.

## Prereqs

- A staging property + pricing plan exist (you already have these from
  Slice 1 + 5 + 6 verification).
- An owner persona is signed in so admin/reports auth passes.

## Seed SQL

Substitute `{PROPERTY_ID}` with the property GUID from
`/admin/properties` and `{OWNER_USER_ID}` with the matching owner.

```sql
-- 1. Five confirmed bookings spread over the last 30 days so Revenue +
-- ADR have data. ConfirmedAt is what Revenue buckets on.
INSERT INTO booking.bookings
  ("Id", reference, property_id, property_title, guest_user_id,
   guest_display_name, checkin_date, checkout_date, guest_count,
   status, source, currency, subtotal, fees, taxes, discount, total,
   cancellation_policy, confirmed_at, created_at, updated_at)
SELECT
  gen_random_uuid(),
  'VRB-S7-' || lpad(s::text, 4, '0'),
  '{PROPERTY_ID}'::uuid,
  'Beach Villa',
  '{OWNER_USER_ID}'::uuid,
  'Seed Guest',
  (CURRENT_DATE - INTERVAL '30 days' + (s * INTERVAL '5 days'))::date,
  (CURRENT_DATE - INTERVAL '30 days' + (s * INTERVAL '5 days') + INTERVAL '3 days')::date,
  2, 'Completed', 'Direct', 'USD',
  900, 100, 50, 0, 1050, 'Moderate',
  (CURRENT_DATE - INTERVAL '30 days' + (s * INTERVAL '5 days'))::timestamptz,
  (CURRENT_DATE - INTERVAL '30 days' + (s * INTERVAL '5 days'))::timestamptz,
  (CURRENT_DATE - INTERVAL '30 days' + (s * INTERVAL '5 days'))::timestamptz
FROM generate_series(0, 4) s
ON CONFLICT DO NOTHING;

-- 2. Three external reservations across two channels for the Source tab.
INSERT INTO sync.external_reservations
  ("Id", channel_feed_id, property_id, channel, i_cal_uid,
   checkin, checkout, raw_payload, imported_at, created_at, updated_at)
VALUES
  (gen_random_uuid(), gen_random_uuid(), '{PROPERTY_ID}'::uuid, 'AirBnb',
   'airbnb-seed-001', CURRENT_DATE - 20, CURRENT_DATE - 17,
   '{}', NOW(), NOW(), NOW()),
  (gen_random_uuid(), gen_random_uuid(), '{PROPERTY_ID}'::uuid, 'AirBnb',
   'airbnb-seed-002', CURRENT_DATE - 10, CURRENT_DATE - 7,
   '{}', NOW(), NOW(), NOW()),
  (gen_random_uuid(), gen_random_uuid(), '{PROPERTY_ID}'::uuid, 'Vrbo',
   'vrbo-seed-001', CURRENT_DATE - 5, CURRENT_DATE - 2,
   '{}', NOW(), NOW(), NOW())
ON CONFLICT DO NOTHING;

-- 3. One availability block so Occupancy's denominator drops on those days.
INSERT INTO booking.availability_blocks
  ("Id", property_id, start_date, end_date, reason, created_at, updated_at)
VALUES (gen_random_uuid(), '{PROPERTY_ID}'::uuid,
        CURRENT_DATE - 15, CURRENT_DATE - 12,
        'Maintenance — Slice 7 seed', NOW(), NOW())
ON CONFLICT DO NOTHING;
```

## Verify

```bash
# Expect ~30 daily points; some non-zero booked + available nights.
curl -sS -H "Authorization: Bearer <dev-token>" \
  "https://ca-vrbook-api-staging.../api/v1/admin/reports/occupancy?from=$(date -d '30 days ago' +%F)&to=$(date +%F)" \
  | jq '.summary'

# Expect totalRevenue > 0; confirmedBookings >= 5.
curl -sS -H "Authorization: Bearer <dev-token>" \
  ".../api/v1/admin/reports/revenue?from=...&to=..." | jq '.summary'

# Expect at least Direct + AirBnB slices with non-zero bookings.
curl -sS -H "Authorization: Bearer <dev-token>" \
  ".../api/v1/admin/reports/source?from=...&to=..." | jq '.slices'
```

## Undo

```sql
DELETE FROM booking.bookings WHERE reference LIKE 'VRB-S7-%';
DELETE FROM sync.external_reservations WHERE i_cal_uid LIKE '%-seed-%';
DELETE FROM booking.availability_blocks
  WHERE reason = 'Maintenance — Slice 7 seed';
```
