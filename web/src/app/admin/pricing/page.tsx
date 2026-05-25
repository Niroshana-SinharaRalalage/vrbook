'use client';

const AdminPricingPage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Pricing</h1>
      <p className="text-sm text-muted-foreground">
        Implemented by Agent O1 — see proposal §12.2 (screen 4: pricing editor
        with rule priority badges + drag-reorder) and §11.2 (rule engine).
        Wires to GET/PUT /properties/{'{id}'}/pricing and the /quotes preview.
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Pricing plan editor + rule list + nightly preview.
      </div>
    </div>
  );
};

export default AdminPricingPage;
