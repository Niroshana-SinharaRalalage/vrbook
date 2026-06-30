'use client';

/**
 * Slice OPS.M.7 §3.4 (D4) + §3.7 (D7) — landing page after Stripe redirects
 * the user back from the hosted onboarding form. Polls `useMyTenant()` at
 * 1Hz until `isComplete` flips (typically <5s; the OPS.M.5 webhook handler
 * applies readiness on `account.updated`) OR the 30-attempt cap exhausts.
 */
import Link from 'next/link';
import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { CheckCircle2, RotateCw } from 'lucide-react';
import { useMyTenant } from '@/hooks/useMyTenant';
import { cn } from '@/lib/utils/cn';

const OnboardingCompletePage = () => {
  const router = useRouter();
  const { data, isExhausted, refetch, pollAttempts } = useMyTenant({
    pollIntervalMs: 1000,
    pollMax: 30,
    stopWhen: (t) => t.onboarding.isComplete || t.status === 'Suspended',
  });

  useEffect(() => {
    if (data?.onboarding.isComplete) {
      // Auto-bounce to dashboard once the wizard is done; user can also
      // click the explicit CTA below.
      const id = setTimeout(() => router.replace('/admin'), 1500);
      return () => clearTimeout(id);
    }
    return undefined;
  }, [data?.onboarding.isComplete, router]);

  const isDone = data?.onboarding.isComplete === true;
  const isSuspended = data?.status === 'Suspended';

  return (
    <div className="mx-auto max-w-xl space-y-4 py-10">
      <h1 className="text-xl font-semibold">
        {isDone ? "You're all set." : isSuspended ? 'Action required' : 'Verifying Stripe…'}
      </h1>

      <div
        role="status"
        aria-live="polite"
        className={cn(
          'rounded-md border p-4 text-sm',
          isDone
            ? 'border-emerald-300 bg-emerald-50/40 text-emerald-900'
            : isSuspended
              ? 'border-amber-300 bg-amber-50/40 text-amber-900'
              : 'border-border bg-muted/30 text-muted-foreground',
        )}
      >
        {isDone ? (
          <span className="inline-flex items-center gap-2">
            <CheckCircle2 className="h-5 w-5 text-emerald-600" aria-hidden />
            Stripe activated charges and payouts. Redirecting to your dashboard…
          </span>
        ) : isSuspended ? (
          <>
            Stripe needs more information before payouts can resume. Re-run
            onboarding to clear it.
          </>
        ) : (
          <>
            Polling Stripe for confirmation… attempt {pollAttempts}/30.
          </>
        )}
      </div>

      {isExhausted && !isDone && (
        <div className="rounded-md border border-border p-4 text-sm">
          <p>
            We haven&apos;t seen Stripe activate yet. This is usually a brief delay;
            most accounts activate within a minute.
          </p>
          <div className="mt-3 flex gap-2">
            <button
              type="button"
              onClick={() => {
                void refetch();
              }}
              className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-3 py-2 text-sm font-medium text-white hover:bg-brand-maroon-700"
            >
              <RotateCw className="h-4 w-4" aria-hidden />
              Refresh now
            </button>
            <Link
              href="/admin/onboarding"
              className="rounded-md border border-border px-3 py-2 text-sm hover:bg-accent"
            >
              Back to onboarding
            </Link>
          </div>
        </div>
      )}

      {isSuspended && (
        <Link
          href="/admin/onboarding"
          className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-maroon-700"
        >
          Re-run Stripe onboarding
        </Link>
      )}
    </div>
  );
};

export default OnboardingCompletePage;
