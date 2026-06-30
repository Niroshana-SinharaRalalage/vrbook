/**
 * Slice OPS.M.10.2 F11.7.4.1 — architecture guardrail.
 *
 * Every client component that calls an authenticated API helper MUST
 * go through `useAuthedQuery`. Bare `useQuery({ queryFn: <helper> })`
 * is forbidden for helpers in the `authedHelpers` list. Server
 * components (no `'use client'`) MAY NOT import any authed helper.
 *
 * Catches BOTH the SSR-auth bug AND the MSAL race in CI before merge.
 * See `docs/OPS_M_10_2_F11_WEB_AUDIT.md` §Part 3.4 for rationale.
 *
 * Maintenance: when a new authed helper lands in `web/src/lib/api/*.ts`,
 * add it to `authedHelpers` below. The audit doc §Part 4.5 covers the
 * `anonymous: true` opt-out for genuinely anonymous endpoints.
 *
 * IMPORTANT regex note: vitest's swc transformer doubles backslashes
 * inside template literals (a `\\b` template ends up as `\\\\b` in the
 * compiled regex source, NOT the intended `\b` word-boundary escape).
 * The constructed regex then silently matches nothing — that was the
 * silent-pass bug shipped in the v0 of this test. Plain string concat
 * of a `'\\b'` constant sidesteps the transformer and works in both
 * vitest and plain Node.
 */
import { readFileSync } from 'node:fs';
import fg from 'fast-glob';
import { describe, expect, it } from 'vitest';

const authedHelpers = [
  'getCurrentUser', 'getMyTenant', 'getMyLoyalty',
  'myBookings', 'getBooking',
  'adminListBookings', 'adminGetBooking',
  'adminListMyProperties', 'adminGetPropertyById',
  'createProperty', 'updateProperty',
  'listThreads', 'getThread', 'listMessages', 'sendMessage', 'markThreadRead',
  'listPlatformTenants', 'getPlatformTenant', 'suspendTenant', 'reactivateTenant', 'setPlatformFee',
  'submitReview', 'respondToReview', 'adminListReviews', 'adminHideReview', 'adminRestoreReview', 'adminRejectReview',
  'listChannelFeeds', 'createChannelFeed', 'updateChannelFeed', 'deleteChannelFeed',
  'listSyncConflicts', 'resolveSyncConflict',
  'adminListAmenities', 'adminCreateAmenity', 'adminUpdateAmenity', 'adminDisableAmenity', 'adminEnableAmenity', 'adminDeleteAmenity',
  'adminListNotifications', 'adminRetryNotification',
  'getOccupancyReport', 'getRevenueReport', 'getAdrReport', 'getSourceReport',
  'createHold', 'releaseHold', 'placeBooking', 'cancelBooking',
  'confirmBooking', 'rejectBooking', 'checkInBooking', 'checkOutBooking',
  'getPaymentIntentForBooking',
  'getPropertyCalendar', 'listAvailabilityBlocks', 'createAvailabilityBlock', 'deleteAvailabilityBlock',
];

const WB = '\\b';

// F11.7.4 migration scaffold: these client components are pre-existing
// bug-class-2 offenders queued for migration in later sub-slices. SHRINK
// THIS LIST as each migration lands; it MUST be empty by the end of
// F11.7.4.7. Adding a file here for a NEW component is the regression
// this test is designed to prevent — don't do it.
//
// F11.7.4.3 + F11.7.4.4 cleared the original 3 entries (useMyTenant +
// the two platform tenants pages). Empty now.
const KNOWN_PENDING_CLIENT_OFFENDERS = new Set<string>();

// vitest's process.cwd() is the project that holds vitest.config —
// here that's `web/`. fast-glob + readFileSync below are relative to
// that cwd.
describe('auth arch', () => {
  it('no client component uses raw useQuery() with an authed helper', async () => {
    const files = await fg(['src/**/*.{ts,tsx}'], {
      ignore: [
        '**/node_modules/**',
        '**/*.test.*',
        '**/useAuthedQuery.ts',
      ],
    });
    const offenders: string[] = [];
    for (const f of files) {
      if (KNOWN_PENDING_CLIENT_OFFENDERS.has(f.replace(/\\/g, '/'))) continue;
      const text = readFileSync(f, 'utf8');
      if (!text.includes("from '@tanstack/react-query'")) continue;
      if (!/\buseQuery\s*\(/.test(text)) continue;
      for (const helper of authedHelpers) {
        const re = new RegExp('queryFn[^,}]*' + WB + helper + WB);
        if (re.test(text)) {
          offenders.push(
            `${f} uses ${helper}() with bare useQuery() — switch to useAuthedQuery from @/hooks/useAuthedQuery`,
          );
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it('no server-component page imports an authed helper', async () => {
    const files = await fg(['src/app/**/{page,layout}.tsx']);
    const offenders: string[] = [];
    for (const f of files) {
      const text = readFileSync(f, 'utf8');
      const isClient = /^\s*['"]use client['"]/.test(text);
      if (isClient) continue;
      for (const helper of authedHelpers) {
        if (new RegExp(WB + helper + WB).test(text)) {
          offenders.push(
            `${f} (server component) imports ${helper} — split into a thin server-shell page + client component`,
          );
        }
      }
    }
    expect(offenders).toEqual([]);
  });
});
