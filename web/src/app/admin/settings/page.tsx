'use client';

const AdminSettingsPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Settings</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §6.2 (GET/PUT /admin/toggles)
        and §11.4 (feature toggle resolution: user → property → global → default).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Feature toggle editor + tenant-wide preferences.
      </div>
    </div>
  );
};

export default AdminSettingsPage;
