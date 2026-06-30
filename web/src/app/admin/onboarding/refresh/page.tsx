'use client';

/**
 * Slice OPS.M.7 §3.4 (D4) — Stripe redirects here when the AccountLink URL
 * has expired (5 minutes). We generate a fresh link and bounce the user
 * back into the hosted form.
 */
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useMyTenant } from '@/hooks/useMyTenant';
import { generateStripeAccountLink } from '@/lib/api/tenant';

const OnboardingRefreshPage = () => {
  const router = useRouter();
  const { data } = useMyTenant();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!data) return;
    let cancelled = false;
    generateStripeAccountLink(data.id)
      .then(({ url }) => {
        if (cancelled) return;
        window.location.href = url;
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setError(e instanceof Error ? e.message : 'Stripe call failed.');
        // Bounce to the wizard so the user has a recovery path.
        router.replace('/admin/onboarding');
      });
    return () => {
      cancelled = true;
    };
  }, [data, router]);

  return (
    <div className="mx-auto max-w-md py-10 text-center">
      <h1 className="text-xl font-semibold">Refreshing your onboarding link…</h1>
      <p className="mt-2 text-sm text-muted-foreground">
        Stripe links expire after 5 minutes. We&apos;ll generate a new one and
        bounce you right back.
      </p>
      {error && (
        <p role="alert" className="mt-4 text-sm text-destructive">
          {error}{' '}
          <Link href="/admin/onboarding" className="underline">
            Back to onboarding
          </Link>
        </p>
      )}
    </div>
  );
};

export default OnboardingRefreshPage;
