'use client';

const AdminSyncPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Sync</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 5: conflict
        resolution modal with three actions) and §8.3 (algorithm). Wires to
        /admin/channel-feeds + /admin/sync-conflicts (§6.2).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Channel-feed config table + unresolved-conflicts queue.
      </div>
    </div>
  );
};

export default AdminSyncPage;
