'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  LayoutDashboard,
  Home,
  CalendarDays,
  ClipboardList,
  DollarSign,
  Users,
  MessageSquare,
  Star,
  BarChart3,
  RefreshCw,
  Settings,
  Sparkles,
  Mail,
  Rocket,
  Shield,
  Building2,
} from 'lucide-react';

import { cn } from '@/lib/utils/cn';
import { useMyTenant } from '@/hooks/useMyTenant';
import { useMe } from '@/hooks/useMe';

/** Admin nav per proposal §12.1. */
type NavItem = {
  href: string;
  label: string;
  icon: typeof LayoutDashboard;
  exact?: boolean;
};

const items: readonly NavItem[] = [
  { href: '/admin', label: 'Dashboard', icon: LayoutDashboard, exact: true },
  { href: '/admin/properties', label: 'Properties', icon: Home },
  { href: '/admin/calendar', label: 'Calendar', icon: CalendarDays },
  { href: '/admin/bookings', label: 'Bookings', icon: ClipboardList },
  { href: '/admin/pricing', label: 'Pricing', icon: DollarSign },
  { href: '/admin/guests', label: 'Guests', icon: Users },
  { href: '/admin/messages', label: 'Messages', icon: MessageSquare },
  { href: '/admin/reviews', label: 'Reviews', icon: Star },
  { href: '/admin/reports', label: 'Reports', icon: BarChart3 },
  { href: '/admin/sync', label: 'Sync', icon: RefreshCw },
  { href: '/admin/notifications', label: 'Notifications', icon: Mail },
  { href: '/admin/amenities', label: 'Amenities', icon: Sparkles },
  // VRB-210 shell dark-launched → re-linked in the shell-integration pass now
  // that the settings area + audit trail (/changes) are live.
  { href: '/admin/settings', label: 'Settings', icon: Settings },
];

const platformItems: readonly NavItem[] = [
  { href: '/admin/platform/tenants', label: 'Tenants', icon: Building2 },
];

export const AdminSidebar = () => {
  const pathname = usePathname();
  const { data: tenant } = useMyTenant();
  const { data: me } = useMe();
  // OPS.M.7 §3.5 + OPS.M.8 §3.9 — hide the wizard nudge when the tenant
  // is Suspended; the operator's Suspend message is the surface that matters.
  const showContinueSetup =
    tenant && !tenant.onboarding.isComplete && tenant.status !== 'Suspended';
  // OPS.M.8 §3.7 + §3.10 — platform group rendered only when the
  // DB-authoritative is_platform_admin bit is true.
  const showPlatform = me?.isPlatformAdmin === true;
  return (
    <aside className="hidden border-r border-border bg-muted/30 md:flex md:w-60 md:flex-col">
      <Link
        href="/"
        className="flex h-16 items-center gap-2 border-b border-border px-4 font-semibold hover:bg-muted/40"
        aria-label="VrBook — back to landing page"
      >
        <span className="inline-block h-5 w-5 rounded bg-brand-maroon-600" aria-hidden />
        VrBook Admin
      </Link>
      <nav className="flex-1 space-y-0.5 p-2" aria-label="Admin">
        {tenant?.status === 'Suspended' && (
          <div
            role="status"
            data-testid="tenant-suspended-banner"
            className="mb-2 rounded-md border border-amber-300 bg-amber-50/40 p-2 text-xs text-amber-900 dark:bg-amber-950/30 dark:text-amber-200"
          >
            Your tenant is suspended. Contact support.
          </div>
        )}
        {showContinueSetup && (
          <Link
            href="/admin/onboarding"
            className="mb-1 flex items-center gap-3 rounded-md border border-brand-orange-200 bg-brand-orange-50 px-3 py-2 text-sm text-brand-maroon-700 hover:bg-brand-orange-100 dark:border-brand-maroon-700 dark:bg-brand-maroon-800/40 dark:text-brand-orange-100"
            data-testid="continue-setup-link"
          >
            <Rocket className="h-4 w-4" aria-hidden />
            Continue setup
          </Link>
        )}
        {items.map(({ href, label, icon: Icon, exact }) => {
          const active = exact ? pathname === href : pathname === href || pathname.startsWith(`${href}/`);
          return (
            <Link
              key={href}
              href={href}
              className={cn(
                'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                active
                  ? 'bg-brand-orange-100 text-brand-maroon-700 dark:bg-brand-maroon-700 dark:text-brand-orange-100'
                  : 'text-muted-foreground hover:bg-accent hover:text-foreground',
              )}
            >
              <Icon className="h-4 w-4" aria-hidden />
              {label}
            </Link>
          );
        })}
        {showPlatform && (
          <div
            className="mt-4 border-t border-border pt-3"
            data-testid="platform-nav-group"
          >
            <div className="mb-1 flex items-center gap-2 px-3 text-xs font-medium uppercase tracking-wide text-muted-foreground">
              <Shield className="h-3.5 w-3.5" aria-hidden />
              Platform
            </div>
            {platformItems.map(({ href, label, icon: Icon }) => {
              const active = pathname === href || pathname.startsWith(`${href}/`);
              return (
                <Link
                  key={href}
                  href={href}
                  className={cn(
                    'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                    active
                      ? 'bg-brand-orange-100 text-brand-maroon-700 dark:bg-brand-maroon-700 dark:text-brand-orange-100'
                      : 'text-muted-foreground hover:bg-accent hover:text-foreground',
                  )}
                >
                  <Icon className="h-4 w-4" aria-hidden />
                  {label}
                </Link>
              );
            })}
          </div>
        )}
      </nav>
    </aside>
  );
};
