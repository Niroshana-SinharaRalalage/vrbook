'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { useMsal } from '@azure/msal-react';

const SignOutPage = () => {
  const { instance } = useMsal();

  useEffect(() => {
    const accounts = instance.getAllAccounts();
    if (accounts.length > 0) {
      void instance.logoutRedirect({ postLogoutRedirectUri: '/' });
    }
  }, [instance]);

  return (
    <main className="flex min-h-dvh items-center justify-center">
      <div className="space-y-3 text-center">
        <h1 className="text-lg font-semibold">Signed out</h1>
        <p className="text-sm text-muted-foreground">You have been signed out.</p>
        <Link
          href="/"
          className="inline-flex h-10 items-center rounded-md bg-brand-orange-600 px-4 text-sm font-medium text-white hover:bg-brand-orange-700"
        >
          Back to home
        </Link>
      </div>
    </main>
  );
};

export default SignOutPage;
