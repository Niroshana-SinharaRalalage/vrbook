'use client';

import { useSettingsAccess } from './useSettingsAccess';

/**
 * VRB-210 — section-level authorization (ADR-0016) on top of `AdminAuthGuard`
 * (which only proves Entra-local admin auth). Tenant sections require a
 * `tenant_admin` membership (a platform-admin also passes); platform sections
 * require `isPlatformAdmin`. Denied → an in-page 403 (not a redirect), so a
 * tenant-admin hitting a platform URL directly is refused.
 */
export const SettingsAccessGate = ({
  require,
  children,
}: {
  readonly require: 'tenant' | 'platform';
  readonly children: React.ReactNode;
}) => {
  const { isPlatformAdmin, isTenantAdmin, isLoading } = useSettingsAccess();

  if (isLoading) {
    return <p className="p-6 text-sm text-muted-foreground">Checking access…</p>;
  }

  const allowed = require === 'platform' ? isPlatformAdmin : isTenantAdmin || isPlatformAdmin;
  if (!allowed) {
    return (
      <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-6">
        <h2 className="text-base font-medium text-destructive">Not authorized</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {require === 'platform'
            ? 'This area is for platform administrators.'
            : 'This area is for tenant administrators.'}
        </p>
      </div>
    );
  }

  return <>{children}</>;
};
