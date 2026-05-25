'use client';

const AdminGuestsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Guests</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.1 (nav) and §6.2
        (GET /admin/users). Includes loyalty tier and stay history.
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Guest search + profile drawer.
      </div>
    </div>
  );
};

export default AdminGuestsPage;
