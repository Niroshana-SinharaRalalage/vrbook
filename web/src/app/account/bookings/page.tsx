import type { Metadata } from 'next';

import { MyBookingsClient } from '@/components/booking/MyBookingsClient';

// Slice OPS.M.10.2 F11.7.3 — thin server shell; list moved to client
// component so the MSAL bearer token flows to /api/v1/bookings.
export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'My bookings',
  robots: { index: false, follow: false },
};

const AccountBookingsPage = () => (
  <div className="space-y-4">
    <h1 className="text-2xl font-semibold tracking-tight">My bookings</h1>
    <MyBookingsClient />
  </div>
);

export default AccountBookingsPage;
