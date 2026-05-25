import { Suspense } from 'react';
import type { Metadata } from 'next';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';

// Booking detail (auth required — guest or owner). UI implemented by Agent F1
// (guest view) + O1 (owner view shares the booking-detail panel from §12 screen 3).

export const metadata: Metadata = {
  title: 'Booking detail',
  robots: { index: false, follow: false },
};

interface BookingDetailPageProps {
  readonly params: { id: string };
}

const BookingSkeleton = () => (
  <div className="space-y-4">
    <div className="h-7 w-1/3 animate-pulse rounded bg-muted" />
    <div className="h-4 w-1/4 animate-pulse rounded bg-muted" />
    <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
      <div className="md:col-span-2 h-64 animate-pulse rounded-xl bg-muted" />
      <div className="h-64 animate-pulse rounded-xl bg-muted" />
    </div>
  </div>
);

const BookingDetail = async ({ id }: { id: string }) => {
  // F1: const booking = await apiFetch<BookingDto>(`/bookings/${id}`);
  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h1 className="text-3xl font-semibold tracking-tight">Booking {id}</h1>
        <p className="text-sm text-muted-foreground">
          Status pill, line items, payments, messages, and timeline — built by Agent F1 against
          GET /bookings/{'{id}'} (proposal §6.2 — Booking).
        </p>
      </header>
      <div className="rounded-xl border border-dashed border-border p-12 text-center text-sm text-muted-foreground">
        Booking state-machine view (proposal §7) renders here.
      </div>
    </div>
  );
};

const BookingDetailPage = ({ params }: BookingDetailPageProps) => {
  return (
    <>
      <SiteHeader />
      <main className="container py-10">
        <Suspense fallback={<BookingSkeleton />}>
          <BookingDetail id={params.id} />
        </Suspense>
      </main>
      <SiteFooter />
    </>
  );
};

export default BookingDetailPage;
