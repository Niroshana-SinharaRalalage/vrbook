/**
 * VRB-210 — standalone field error (for controls not wrapped in the DS `Field`,
 * which already renders its own). `role="alert"` so it's announced; pair its
 * `id` with the control's `aria-describedby`.
 */
export const FieldError = ({ id, children }: { readonly id?: string; readonly children?: React.ReactNode }) =>
  children ? (
    <p id={id} role="alert" className="mt-1 text-xs text-destructive">
      {children}
    </p>
  ) : null;
