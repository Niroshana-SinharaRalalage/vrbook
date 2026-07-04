'use client';

import { LogIn, LogOut } from 'lucide-react';

import { useAuth } from '@/lib/auth/useAuth';
import { cn } from '@/lib/utils/cn';

/**
 * Client island that gives the header a sign-in / sign-out trigger backed by
 * MSAL. When a real Entra session exists, shows a sign-out button. Otherwise
 * shows a sign-in button that triggers `loginRedirect` to the External tenant.
 */
export const SiteHeaderAuth = () => {
  const { isAuthenticated, isBusy, user, signIn, signOut } = useAuth();

  if (isAuthenticated && user) {
    return (
      <button
        type="button"
        onClick={signOut}
        disabled={isBusy}
        className={cn(
          'inline-flex items-center gap-1.5 rounded-md border border-border bg-background px-3 py-1.5 text-sm font-medium text-foreground',
          'transition-colors hover:bg-accent disabled:opacity-50',
        )}
        title={user.email ? `Signed in as ${user.email}` : 'Sign out'}
      >
        <LogOut className="h-4 w-4" aria-hidden />
        <span>Sign out</span>
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={signIn}
      disabled={isBusy}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md bg-brand-orange-600 px-3 py-1.5 text-sm font-medium text-white',
        'transition-colors hover:bg-brand-orange-700 disabled:opacity-50',
      )}
    >
      <LogIn className="h-4 w-4" aria-hidden />
      <span>Sign in</span>
    </button>
  );
};
