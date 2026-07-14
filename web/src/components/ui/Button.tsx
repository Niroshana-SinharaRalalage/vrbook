import { forwardRef, type ButtonHTMLAttributes } from 'react';

import { cn } from '@/lib/utils/cn';

/**
 * VrBook Button — the primary action primitive.
 *
 * Design system notes:
 *  - `primary` is brand orange (the single "do it" colour); `secondary` is
 *    brand maroon (authority). Everything else is quiet chrome.
 *  - Every button carries the shared "booking-desk" signature: a 2px orange
 *    focus-visible ring offset from the surface (WCAG 2.2 AA 2.4.11/2.4.13,
 *    not colour-reliant) and a tactile `active:translate-y-px` press that
 *    collapses under `prefers-reduced-motion`.
 *  - Defaults to `type="button"` so a button dropped inside a form never
 *    submits it by accident — pass `type="submit"` deliberately.
 */

export type ButtonVariant =
  | 'primary'
  | 'secondary'
  | 'outline'
  | 'ghost'
  | 'destructive'
  | 'link';
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon';

const base =
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium ' +
  'ring-offset-background transition-colors motion-reduce:transition-none ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 ' +
  'disabled:pointer-events-none disabled:opacity-50 ' +
  'active:translate-y-px motion-reduce:active:translate-y-0';

const variantClasses: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-primary-foreground hover:bg-primary/90',
  secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/90',
  outline: 'border border-input bg-background hover:bg-accent hover:text-accent-foreground',
  ghost: 'hover:bg-accent hover:text-accent-foreground',
  destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
  link: 'text-primary underline-offset-4 hover:underline',
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'h-9 rounded-md px-3',
  md: 'h-10 px-4 py-2',
  lg: 'h-11 rounded-md px-8',
  icon: 'h-10 w-10',
};

export interface ButtonVariantsOptions {
  readonly variant?: ButtonVariant;
  readonly size?: ButtonSize;
  readonly className?: string;
}

/** Class-string escape hatch so an `<a>` (or Radix trigger) can borrow the look. */
export const buttonVariants = ({
  variant = 'primary',
  size = 'md',
  className,
}: ButtonVariantsOptions = {}): string =>
  cn(base, variantClasses[variant], sizeClasses[size], className);

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  readonly variant?: ButtonVariant;
  readonly size?: ButtonSize;
  /** Shows a spinner, sets `aria-busy`, and disables the button. */
  readonly loading?: boolean;
}

const Spinner = () => (
  <svg
    data-testid="button-spinner"
    aria-hidden="true"
    className="h-4 w-4 animate-spin"
    viewBox="0 0 24 24"
    fill="none"
  >
    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
    <path
      className="opacity-90"
      fill="currentColor"
      d="M4 12a8 8 0 0 1 8-8V0C5.373 0 0 5.373 0 12h4z"
    />
  </svg>
);

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  (
    { variant = 'primary', size = 'md', loading = false, disabled, type = 'button', className, children, ...props },
    ref,
  ) => (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={buttonVariants({ variant, size, className })}
      {...props}
    >
      {loading && <Spinner />}
      {children}
    </button>
  ),
);
Button.displayName = 'Button';
