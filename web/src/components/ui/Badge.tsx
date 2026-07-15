import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils/cn';

/**
 * VrBook Badge — a status pill.
 *
 * The variant set is the booking-status vocabulary: `success` (confirmed /
 * paid), `warning` (pending / action-needed), `destructive` (rejected /
 * cancelled). Rendered "soft" — a tint of the status colour with the
 * full-strength colour as text and a hairline ring — so it reads AA on the
 * stone canvas in both themes without shouting. `tabular-nums` keeps counts
 * and prices aligned when badges sit in a row.
 */

export type BadgeVariant =
  | 'default'
  | 'secondary'
  | 'outline'
  | 'success'
  | 'warning'
  | 'destructive';

const base =
  'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium tracking-wide tabular-nums';

const variantClasses: Record<BadgeVariant, string> = {
  default: 'bg-primary/10 text-primary ring-1 ring-inset ring-primary/20',
  secondary: 'bg-secondary/10 text-secondary ring-1 ring-inset ring-secondary/20',
  outline: 'text-foreground ring-1 ring-inset ring-border',
  success: 'bg-success/10 text-success ring-1 ring-inset ring-success/25',
  warning: 'bg-warning/10 text-warning ring-1 ring-inset ring-warning/25',
  destructive: 'bg-destructive/10 text-destructive ring-1 ring-inset ring-destructive/25',
};

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  readonly variant?: BadgeVariant;
}

export const Badge = ({ variant = 'default', className, ...props }: BadgeProps) => (
  <span className={cn(base, variantClasses[variant], className)} {...props} />
);
