'use client';

const AdminPropertiesPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Properties</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.1 (nav) and §6.2 (Catalog
        endpoints: POST/PUT /properties, images upload/reorder/delete).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Property list + create/edit drawer.
      </div>
    </div>
  );
};

export default AdminPropertiesPage;
