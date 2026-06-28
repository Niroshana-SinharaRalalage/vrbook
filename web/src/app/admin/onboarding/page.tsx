'use client';

/**
 * Slice OPS.M.7 §3.1 (D1) + Step 7 — Tenant Admin onboarding wizard.
 *
 * Three-step flow:
 *   1. Welcome
 *   2. Create your first property → /admin/properties/new?from=onboarding
 *   3. Connect Stripe to get paid (two-call: onboard + account-link)
 *
 * The active step is server-derived (`tenant.onboarding.nextStep`); the
 * wizard renders one "active" card with primary action + two "pending"
 * cards underneath as preview.
 */
import Link from 'next/link';
import { ExternalLink, ArrowRight } from 'lucide-react';
import { useMyTenant } from '@/hooks/useMyTenant';
import { useStripeOnboardingFlow } from '@/hooks/useStripeOnboardingFlow';
import type { MeTenant } from '@/lib/api/tenant';
import { WizardCard } from '@/components/onboarding/WizardCard';
import { cn } from '@/lib/utils/cn';

const StripeButton = ({ tenant }: { readonly tenant: MeTenant }) => {
  const flow = useStripeOnboardingFlow(tenant);
  return (
    <div>
      <button
        type="button"
        onClick={() => flow.start()}
        disabled={flow.status === 'loading'}
        className={cn(
          'inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-4 py-2 text-sm font-medium text-white shadow-sm',
          'hover:bg-brand-maroon-700 disabled:cursor-not-allowed disabled:opacity-60',
        )}
      >
        {flow.status === 'loading'
          ? 'Redirecting to Stripe…'
          : tenant.hasStripeAccount
            ? 'Continue Stripe onboarding'
            : 'Connect Stripe'}
        <ExternalLink className="h-4 w-4" aria-hidden />
      </button>
      {flow.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {flow.error}
        </p>
      )}
    </div>
  );
};

const OnboardingPage = () => {
  const { data: tenant, isLoading, isError } = useMyTenant();

  if (isLoading) {
    return (
      <div className="mx-auto max-w-2xl py-12 text-center text-muted-foreground">
        Loading your tenant…
      </div>
    );
  }

  if (isError || !tenant) {
    return (
      <div className="mx-auto max-w-2xl py-12">
        <h1 className="text-xl font-semibold">We couldn't load your tenant.</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Reload the page; if the problem persists, contact support.
        </p>
      </div>
    );
  }

  const next = tenant.onboarding.nextStep;
  const stateFor = (
    me: 'Welcome' | 'CreateProperty' | 'ConnectStripe',
  ): 'active' | 'done' | 'pending' => {
    if (next === me) return 'active';
    const order = ['Welcome', 'CreateProperty', 'ConnectStripe'] as const;
    const myIdx = order.indexOf(me);
    const activeIdx = order.indexOf(next as (typeof order)[number]);
    if (activeIdx === -1) return 'done'; // AwaitingVerification or Done
    return myIdx < activeIdx ? 'done' : 'pending';
  };

  return (
    <div className="mx-auto max-w-3xl space-y-6 py-8">
      <header>
        <h1 className="text-2xl font-semibold">Welcome to VrBook, {tenant.displayName}</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Three quick steps to start accepting bookings.
        </p>
      </header>

      {tenant.status === 'Suspended' && (
        <div
          role="alert"
          className="rounded-md border border-amber-300 bg-amber-50/40 p-4 text-sm text-amber-900 dark:bg-amber-950/30 dark:text-amber-200"
        >
          <strong>Action required.</strong> Your Stripe account needs more
          information before payouts resume. Re-run onboarding below to clear it.
        </div>
      )}

      {next === 'AwaitingVerification' && (
        <div
          role="status"
          className="rounded-md border border-sky-300 bg-sky-50/50 p-4 text-sm text-sky-900 dark:bg-sky-950/30 dark:text-sky-200"
        >
          <strong>Stripe is reviewing your details.</strong> Most accounts
          activate within minutes; we'll refresh this page automatically.
        </div>
      )}

      <WizardCard
        step={1}
        total={3}
        title="Welcome"
        state={stateFor('Welcome')}
        description="Tell us about your hosting business so we can tailor the dashboard."
      />

      <WizardCard
        step={2}
        total={3}
        title="Create your first property"
        state={stateFor('CreateProperty')}
        description={`You currently have ${tenant.propertyCount} ${tenant.propertyCount === 1 ? 'property' : 'properties'}.`}
        action={
          stateFor('CreateProperty') === 'active' && (
            <Link
              href="/admin/properties/new?from=onboarding"
              className={cn(
                'inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-4 py-2 text-sm font-medium text-white shadow-sm',
                'hover:bg-brand-maroon-700',
              )}
            >
              Add your first property
              <ArrowRight className="h-4 w-4" aria-hidden />
            </Link>
          )
        }
      />

      <WizardCard
        step={3}
        total={3}
        title="Connect Stripe to get paid"
        state={stateFor('ConnectStripe')}
        description="We use Stripe Connect Express — payouts arrive in your bank account on Stripe's standard schedule."
        action={
          stateFor('ConnectStripe') === 'active' && <StripeButton tenant={tenant} />
        }
      />
    </div>
  );
};

export default OnboardingPage;
