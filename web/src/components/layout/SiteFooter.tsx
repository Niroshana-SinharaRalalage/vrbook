import Link from 'next/link';

import { CookiePreferencesButton } from '@/components/consent/CookiePreferencesButton';

export const SiteFooter = () => {
  const year = new Date().getFullYear();
  return (
    <footer className="mt-16 border-t border-border bg-muted/30">
      <div className="container flex flex-col gap-6 py-10 text-sm text-muted-foreground md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <p className="font-semibold text-foreground">VrBook</p>
          <p>Direct bookings, no service fee.</p>
        </div>
        <nav aria-label="Footer" className="grid grid-cols-2 gap-x-12 gap-y-2 md:grid-cols-3">
          <Link href="/properties" className="hover:text-foreground">
            Browse stays
          </Link>
          <Link href="/account/bookings" className="hover:text-foreground">
            My trips
          </Link>
          <Link href="/account/messages" className="hover:text-foreground">
            Messages
          </Link>
          <Link href="/auth/signout" className="hover:text-foreground">
            Sign out
          </Link>
          {/* VRB-311 — legal + consent surfaces */}
          <Link href="/legal/terms" className="hover:text-foreground">
            Terms
          </Link>
          <Link href="/legal/privacy" className="hover:text-foreground">
            Privacy
          </Link>
          <Link href="/legal/cancellation" className="hover:text-foreground">
            Cancellation
          </Link>
          <CookiePreferencesButton className="text-left hover:text-foreground" />
        </nav>
        <p>&copy; {year} VrBook</p>
      </div>
    </footer>
  );
};
