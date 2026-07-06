import { type ReactNode } from 'react';

import { AdminSidebar } from '@/components/layout/AdminSidebar';
import { AdminAuthGuard } from '@/components/auth/AdminAuthGuard';

interface AdminLayoutProps {
  readonly children: ReactNode;
}

// Admin shell per proposal §12.1: persistent side nav + content. Different from
// the public site chrome (SiteHeader/Footer) by design — the admin is a tool,
// not a marketing surface.
//
// Slice OPS.M.12.6 — wrapped in `<AdminAuthGuard>` so unauthenticated visitors
// redirect to `/auth/signin?flow=admin&returnTo=<pathname>` instead of the
// generic guest sign-in. See ADR-0016.
const AdminLayout = ({ children }: AdminLayoutProps) => {
  return (
    <div className="flex min-h-dvh">
      <AdminSidebar />
      <div className="flex-1 bg-background">
        <header className="flex h-16 items-center border-b border-border px-6">
          <span className="text-sm text-muted-foreground">
            Admin · auth-gated by Agent F1 (proposal §14.2)
          </span>
        </header>
        <main className="p-6">
          <AdminAuthGuard>{children}</AdminAuthGuard>
        </main>
      </div>
    </div>
  );
};

export default AdminLayout;
