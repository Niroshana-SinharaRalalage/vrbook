'use client';

import type { ReactNode } from 'react';

/**
 * Slice OPS.M.10.2 F11.7.5.9 — replaces `window.confirm(...)` popups in
 * operator-facing flows with an inline modal that
 *
 *   - matches the app's visual language (rounded border, backdrop, focus
 *     ring) instead of the browser chrome prompt;
 *   - can show an error banner INSIDE the modal so a failed action doesn't
 *     leak the error to a page-level banner while the modal stays open;
 *   - can be styled per confirm-variant (destructive / success / neutral).
 *
 * Not a full dialog primitive — a5 lands the shared Radix Dialog wrapper if
 * we need focus-trap + escape-key handling across a wider surface. For now
 * this component matches the existing `rejectOpen` modal in
 * `/admin/bookings/[id]/page.tsx` so the two shapes stay consistent.
 */
export interface ConfirmActionModalProps {
  readonly open: boolean;
  readonly title: string;
  readonly description: ReactNode;
  readonly confirmLabel: string;
  readonly busyLabel?: string;
  readonly confirmVariant?: 'destructive' | 'success' | 'default';
  readonly onCancel: () => void;
  readonly onConfirm: () => void;
  readonly busy?: boolean;
  readonly error?: string | null;
}

const variantClasses: Record<NonNullable<ConfirmActionModalProps['confirmVariant']>, string> = {
  destructive: 'bg-red-600 hover:bg-red-700',
  success: 'bg-green-600 hover:bg-green-700',
  default: 'bg-brand-maroon-700 hover:bg-brand-maroon-800',
};

export const ConfirmActionModal = ({
  open,
  title,
  description,
  confirmLabel,
  busyLabel,
  confirmVariant = 'default',
  onCancel,
  onConfirm,
  busy = false,
  error,
}: ConfirmActionModalProps) => {
  if (!open) return null;
  const confirmClass = variantClasses[confirmVariant];
  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-action-title"
    >
      <div className="w-full max-w-md rounded-xl border border-border bg-background p-6 shadow-2xl">
        <h3 id="confirm-action-title" className="text-base font-medium">{title}</h3>
        <div className="mt-2 text-sm text-muted-foreground">{description}</div>

        {error && (
          <div className="mt-4 rounded-md border border-destructive/40 bg-destructive/5 p-3 text-xs text-destructive">
            {error}
          </div>
        )}

        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-md border border-input bg-background px-4 py-2 text-sm hover:bg-accent disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className={`rounded-md px-4 py-2 text-sm font-medium text-white disabled:opacity-50 ${confirmClass}`}
          >
            {busy && busyLabel ? busyLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
};
