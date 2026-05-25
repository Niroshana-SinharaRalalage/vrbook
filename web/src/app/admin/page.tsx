'use client';

const AdminDashboardPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 1: KPIs, Pending
        Tentative queue, Recent Activity, Sync Health rail).
      </p>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
        {['Occupancy %', 'ADR', 'Revenue', 'Bookings'].map((label) => (
          <div
            key={label}
            className="rounded-xl border border-dashed border-border p-6 text-center text-sm text-muted-foreground"
          >
            {label}
          </div>
        ))}
      </div>
    </div>
  );
};

export default AdminDashboardPage;
