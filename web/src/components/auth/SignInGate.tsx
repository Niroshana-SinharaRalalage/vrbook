'use client';

import { LogIn } from 'lucide-react';
import { useAuth } from '@/lib/auth/useAuth';

/**
 * Slice OPS.M.10.2 F11.7.4.1 — standard empty state for the
 * `needsSignIn` branch of `useAuthedQuery`. Renders a centered card
 * with a Sign-in CTA that triggers MSAL `loginRedirect` (returns to
 * the current route on success).
 */
export const SignInGate = ({
  title,
  description,
}: {
  readonly title: string;
  readonly description?: string;
}) => {
  const { signIn } = useAuth();
  return (
    <div className="mx-auto max-w-md py-12 text-center">
      <h1 className="text-xl font-semibold">{title}</h1>
      {description && <p className="mt-2 text-sm text-muted-foreground">{description}</p>}
      <button
        type="button"
        onClick={signIn}
        className="mt-4 inline-flex items-center gap-1.5 rounded-md bg-brand-orange-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-orange-700"
      >
        <LogIn className="h-4 w-4" aria-hidden /> Sign in
      </button>
    </div>
  );
};
