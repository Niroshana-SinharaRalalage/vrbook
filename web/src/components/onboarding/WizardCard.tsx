/**
 * Slice OPS.M.7 §3.6 + §3.11 — minimal step-card primitive used by the
 * onboarding wizard. Plain Tailwind + Lucide; no new component library
 * dependency.
 */
import type { ReactNode } from 'react';
import { Check } from 'lucide-react';
import { cn } from '@/lib/utils/cn';

export interface WizardCardProps {
  readonly step: number;
  readonly total: number;
  readonly title: string;
  readonly state: 'active' | 'done' | 'pending';
  readonly description?: ReactNode;
  readonly action?: ReactNode;
}

export const WizardCard = ({
  step,
  total,
  title,
  state,
  description,
  action,
}: WizardCardProps) => (
  <section
    aria-labelledby={`wizard-step-${step}-title`}
    aria-current={state === 'active' ? 'step' : undefined}
    className={cn(
      'rounded-lg border p-6 transition-colors',
      state === 'active'
        ? 'border-brand-maroon-300 bg-card shadow-sm'
        : state === 'done'
          ? 'border-emerald-300/60 bg-emerald-50/30 dark:bg-emerald-950/20'
          : 'border-border bg-muted/30',
    )}
  >
    <header className="mb-3 flex items-center gap-3">
      <span
        aria-hidden
        className={cn(
          'flex h-7 w-7 items-center justify-center rounded-full text-xs font-medium',
          state === 'done'
            ? 'bg-emerald-600 text-white'
            : state === 'active'
              ? 'bg-brand-maroon-600 text-white'
              : 'bg-muted text-muted-foreground',
        )}
      >
        {state === 'done' ? <Check className="h-3.5 w-3.5" /> : step}
      </span>
      <span className="text-xs uppercase tracking-wide text-muted-foreground">
        Step {step} of {total}
      </span>
    </header>
    <h2
      id={`wizard-step-${step}-title`}
      className={cn(
        'text-lg font-semibold',
        state === 'pending' && 'text-muted-foreground',
      )}
    >
      {title}
    </h2>
    {description && (
      <div className="mt-2 text-sm text-muted-foreground">{description}</div>
    )}
    {action && <div className="mt-4">{action}</div>}
  </section>
);
