import Link from 'next/link';
import { type ReactNode } from 'react';

import { SiteHeader } from '@/components/layout/SiteHeader';
import { SiteFooter } from '@/components/layout/SiteFooter';

// Slice OPS.M.10.2 F11.7.5.4 — `/account/loyalty` was reachable only by
// typing the URL until F11.7.5.4 added it to this nav. Loyalty is a
// status surface (tier + discount + stays-to-go) rather than a primary
// workflow, so it sits after Profile.
const accountNav = [
  { href: '/account/bookings', label: 'My bookings' },
  { href: '/account/messages', label: 'Messages' },
  { href: '/account/profile', label: 'Profile' },
  { href: '/account/loyalty', label: 'Loyalty' },
] as const;

interface AccountLayoutProps {
  readonly children: ReactNode;
}

const AccountLayout = ({ children }: AccountLayoutProps) => {
  return (
    <>
      <SiteHeader />
      <main className="container py-10">
        <div className="grid grid-cols-1 gap-8 md:grid-cols-[200px_1fr]">
          <aside>
            <nav aria-label="Account" className="space-y-1">
              {accountNav.map(({ href, label }) => (
                <Link
                  key={href}
                  href={href}
                  className="block rounded-md px-3 py-2 text-sm text-muted-foreground transition hover:bg-accent hover:text-foreground"
                >
                  {label}
                </Link>
              ))}
            </nav>
          </aside>
          <section className="min-w-0">{children}</section>
        </div>
      </main>
      <SiteFooter />
    </>
  );
};

export default AccountLayout;
