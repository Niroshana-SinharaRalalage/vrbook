'use client';

import {
  cloneElement,
  forwardRef,
  isValidElement,
  useId,
  type LabelHTMLAttributes,
  type ReactElement,
  type ReactNode,
} from 'react';

import { cn } from '@/lib/utils/cn';

/**
 * VrBook form primitives.
 *
 * `Label` is the standalone styled `<label>`. `Field` is the accessible
 * wrapper that does the wiring humans forget: it links the label to the
 * control (`htmlFor`/`id`), points `aria-describedby` at the description AND
 * the error (only the ones that actually render — never a dangling id), sets
 * `aria-invalid` when there's an error, and `aria-required` when required.
 * The error is surfaced by an `role="alert"` node so it's announced, and by
 * the destructive border on the control — never colour alone.
 *
 * Usage (plays directly with react-hook-form):
 *   <Field label="Email" description="…" error={errors.email?.message}>
 *     <Input type="email" {...register('email')} />
 *   </Field>
 */

const labelBase =
  'text-sm font-medium leading-none text-foreground peer-disabled:cursor-not-allowed peer-disabled:opacity-70';

export const Label = forwardRef<HTMLLabelElement, LabelHTMLAttributes<HTMLLabelElement>>(
  ({ className, ...props }, ref) => (
    <label ref={ref} className={cn(labelBase, className)} {...props} />
  ),
);
Label.displayName = 'Label';

type ControlAriaProps = {
  id?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean | 'true' | 'false';
  'aria-required'?: boolean;
};

export interface FieldProps {
  readonly label?: ReactNode;
  readonly description?: ReactNode;
  /** When set, the control goes invalid and this message is announced. */
  readonly error?: ReactNode;
  readonly required?: boolean;
  readonly className?: string;
  /** Exactly one control element (Input, Select, …). */
  readonly children: ReactElement<ControlAriaProps>;
}

const joinIds = (...ids: Array<string | undefined | false>): string | undefined => {
  const list = ids.filter((v): v is string => Boolean(v));
  return list.length > 0 ? Array.from(new Set(list.join(' ').split(' '))).join(' ') : undefined;
};

export const Field = ({ label, description, error, required, className, children }: FieldProps) => {
  const base = useId();
  const controlId = `${base}-control`;
  const descriptionId = `${base}-description`;
  const errorId = `${base}-error`;
  const hasError = error !== undefined && error !== null && error !== false;

  const control = isValidElement(children)
    ? cloneElement(children, {
        id: children.props.id ?? controlId,
        'aria-describedby': joinIds(
          children.props['aria-describedby'],
          description ? descriptionId : undefined,
          hasError ? errorId : undefined,
        ),
        'aria-invalid': hasError ? 'true' : children.props['aria-invalid'],
        'aria-required': required ? true : children.props['aria-required'],
      })
    : children;

  const resolvedControlId = (isValidElement(children) && children.props.id) || controlId;

  return (
    <div className={cn('space-y-2', className)}>
      {label !== undefined && (
        <Label htmlFor={resolvedControlId}>
          {label}
          {required && (
            <span data-testid="field-required-marker" aria-hidden="true" className="ml-0.5 text-destructive">
              *
            </span>
          )}
        </Label>
      )}
      {control}
      {description && (
        <p id={descriptionId} className="text-sm text-muted-foreground">
          {description}
        </p>
      )}
      {hasError && (
        <p id={errorId} role="alert" className="text-sm font-medium text-destructive">
          {error}
        </p>
      )}
    </div>
  );
};
