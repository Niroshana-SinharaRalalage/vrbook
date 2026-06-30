import type { Metadata } from 'next';

import { SiteFooter } from '@/components/layout/SiteFooter';
import { SiteHeader } from '@/components/layout/SiteHeader';
import { BookingDetailClient } from '@/components/booking/BookingDetailClient';

// Slice OPS.M.10.2 F11.7.3 — page is now a thin server shell; the
// actual booking detail render moved to BookingDetailClient (client
// component) so the MSAL bearer token flows through to the
// [Authorize]'d /api/v1/bookings/{id} call. Pre-fix the SSR fetch
// 401'd, safeFetch only swallowed 404/403, and Next.js crashed with
// 'Application error: server-side exception'.
export const dynamic = 'force-dynamic';

export const metadata: Metadata = {
  title: 'Booking detail',
  robots: { index: false, follow: false },
};

interface BookingDetailPageProps {
  readonly params: { id: string };
}

const BookingDetailPage = ({ params }: BookingDetailPageProps) => (
  <>
    <SiteHeader />
    <main className="container py-10">
      <BookingDetailClient id={params.id} />
    </main>
    <SiteFooter />
  </>
);

export default BookingDetailPage;
