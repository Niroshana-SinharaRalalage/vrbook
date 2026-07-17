import { SiteFooter } from '@/components/layout/SiteFooter';
import { SiteHeader } from '@/components/layout/SiteHeader';

/**
 * VRB-311 — shared chrome for the legal surfaces (/legal/*). Header + footer +
 * a readable measure. Static (RSC, no client JS).
 */
export default function LegalLayout({ children }: { readonly children: React.ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col">
      <SiteHeader />
      <main id="main-content" className="container max-w-3xl flex-1 py-10 md:py-14">
        {children}
      </main>
      <SiteFooter />
    </div>
  );
}
