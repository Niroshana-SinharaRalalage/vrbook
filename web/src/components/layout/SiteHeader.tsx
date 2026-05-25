import Link from 'next/link';
import { Search, User, Calendar } from 'lucide-react';

import { cn } from '@/lib/utils/cn';

const navLinks = [
  { href: '/properties', label: 'Stays', icon: Search },
  { href: '/account/bookings', label: 'My trips', icon: Calendar },
  { href: '/account/profile', label: 'Account', icon: User },
] as const;

export const SiteHeader = () => {
  return (
    <header className="sticky top-0 z-40 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="container flex h-16 items-center justify-between">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <span className="inline-block h-6 w-6 rounded bg-brand-orange-600" aria-hidden />
          <span className="text-brand-maroon-700 dark:text-brand-orange-500">VrBook</span>
        </Link>

        <nav className="hidden items-center gap-6 md:flex" aria-label="Primary">
          {navLinks.map(({ href, label, icon: Icon }) => (
            <Link
              key={href}
              href={href}
              className={cn(
                'flex items-center gap-1.5 text-sm text-muted-foreground transition-colors',
                'hover:text-brand-orange-600 focus-visible:text-brand-orange-600',
              )}
            >
              <Icon className="h-4 w-4" aria-hidden />
              {label}
            </Link>
          ))}
        </nav>
      </div>
    </header>
  );
};
