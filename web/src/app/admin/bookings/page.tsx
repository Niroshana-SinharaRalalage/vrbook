'use client';

const AdminBookingsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Bookings</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 3: detail with
        Overview / Payments / Messages / Timeline tabs). Wires to
        GET /admin/bookings/queue and POST /admin/bookings/manual (§6.2).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Booking table + tentative queue with Confirm/Reject inline actions.
      </div>
    </div>
  );
};

export default AdminBookingsPage;
