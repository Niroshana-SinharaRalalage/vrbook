'use client';

const AccountBookingsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">My bookings</h1>
      <p className="text-sm text-muted-foreground">
        List of the signed-in guest's bookings — implemented by Agent F1. Wires to
        GET /bookings (proposal §6.2 — Booking).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Booking list + status chips render here.
      </div>
    </div>
  );
};

export default AccountBookingsPage;
