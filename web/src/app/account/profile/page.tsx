'use client';

const AccountProfilePage = () => {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Profile</h1>
      <p className="text-sm text-muted-foreground">
        Profile edit form — implemented by Agent F1. Wires to PUT /me
        (proposal §6.2 — Identity / Loyalty / Admin).
      </p>
      <div className="rounded-xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Name, email, locale, loyalty tier, saved payment methods.
      </div>
    </div>
  );
};

export default AccountProfilePage;
