'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';

import { cn } from '@/lib/utils/cn';
import { SETTINGS_SECTIONS, type SettingsNavItem } from './sections';
import { useSettingsAccess } from './useSettingsAccess';

/**
 * VRB-210 — the shared settings chrome: a role-filtered section nav (sidebar
 * ≥ md, an accessible dropdown < md — fixes G19 within settings) and the page
 * title, rendered inside the existing admin shell. Each panel supplies its own
 * sticky `SaveBar` (driven by its `useSettingsForm`).
 */
export const SettingsLayout = ({
  title,
  children,
}: {
  readonly title: string;
  readonly children: React.ReactNode;
}) => {
  const pathname = usePathname();
  const router = useRouter();
  const { isPlatformAdmin, isTenantAdmin } = useSettingsAccess();

  const visible = SETTINGS_SECTIONS.filter(
    (s) => s.ready && (s.scope === 'platform' ? isPlatformAdmin : isTenantAdmin),
  );
  const isActive = (item: SettingsNavItem) => pathname === item.href || pathname.startsWith(`${item.href}/`);

  return (
    <div className="grid gap-6 md:grid-cols-[220px_1fr]">
      {/* Mobile: section dropdown (< md) */}
      <div className="md:hidden">
        <label htmlFor="settings-section" className="sr-only">
          Settings section
        </label>
        <select
          id="settings-section"
          value={visible.find(isActive)?.href ?? ''}
          onChange={(e) => router.push(e.target.value)}
          className="min-h-11 w-full rounded-md border border-input bg-background px-3 text-sm"
        >
          {visible.map((s) => (
            <option key={s.href} value={s.href}>
              {s.label}
            </option>
          ))}
        </select>
      </div>

      {/* Desktop: section sidebar (≥ md) */}
      <nav aria-label="Settings sections" className="hidden md:block">
        <ul className="space-y-1">
          {visible.map((s) => (
            <li key={s.href}>
              <Link
                href={s.href}
                aria-current={isActive(s) ? 'page' : undefined}
                className={cn(
                  'block rounded-md px-3 py-2 text-sm',
                  isActive(s)
                    ? 'bg-accent font-medium text-accent-foreground'
                    : 'text-muted-foreground hover:bg-accent/50 hover:text-foreground',
                )}
              >
                {s.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      <section aria-labelledby="settings-title" className="min-w-0">
        <h1 id="settings-title" className="mb-6 text-2xl font-semibold tracking-tight">
          {title}
        </h1>
        {children}
      </section>
    </div>
  );
};
