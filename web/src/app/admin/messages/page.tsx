'use client';

const AdminMessagesPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Messages</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §10.5 (admin can view any thread
        read-only with filters: property, date range, flagged). Wires to
        GET /admin/threads.
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Thread moderation table + read-only viewer.
      </div>
    </div>
  );
};

export default AdminMessagesPage;
