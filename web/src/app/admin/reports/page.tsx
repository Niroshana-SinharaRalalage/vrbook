'use client';

const AdminReportsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Reports</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 6: Occupancy /
        Revenue / ADR / Booking Source tabs with CSV export). Wires to
        GET /admin/reports/{'{occupancy|revenue|adr}'} (§6.2).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Charts + tabular view + export button.
      </div>
    </div>
  );
};

export default AdminReportsPage;
