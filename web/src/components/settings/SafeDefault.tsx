/**
 * VRB-210 — documents a field's safe default beneath the control. The default is
 * shown as guidance (and used as the form's placeholder/prefill); it's only
 * persisted when the user saves, per the "safe default confirmed on save" AC.
 */
export const SafeDefault = ({ children }: { readonly children: React.ReactNode }) => (
  <p className="mt-1 text-xs text-muted-foreground">
    Safe default: <span className="font-medium">{children}</span>
  </p>
);
