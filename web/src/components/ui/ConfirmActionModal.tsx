'use client';

import type { ReactNode } from 'react';

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from './Dialog';
import { Button, type ButtonVariant } from './Button';

/**
 * Inline confirm modal for operator-facing flows — a styled replacement for
 * `window.confirm(...)` that can surface an error banner INSIDE the modal.
 *
 * VRB-110 — migrated onto the shared `Dialog` primitive, so it now inherits the
 * full modal a11y contract it previously lacked: focus-trap, Escape + outside-
 * click to close (both suppressed while an action is in flight), and
 * focus-return to the trigger. The prop API is unchanged, so existing callers
 * (e.g. `/admin/bookings/[id]`) keep working.
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

const confirmButtonVariant: Record<
  NonNullable<ConfirmActionModalProps['confirmVariant']>,
  ButtonVariant
> = {
  destructive: 'destructive',
  success: 'success',
  default: 'secondary',
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
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        // Ignore close attempts while the action is running.
        if (!next && !busy) onCancel();
      }}
    >
      <DialogContent
        className="max-w-md"
        // While busy, don't let Escape / outside-click cancel a running action.
        onEscapeKeyDown={busy ? (e) => e.preventDefault() : undefined}
        onInteractOutside={busy ? (e) => e.preventDefault() : undefined}
      >
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        {error && (
          <div
            role="alert"
            className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-xs text-destructive"
          >
            {error}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button variant={confirmButtonVariant[confirmVariant]} onClick={onConfirm} loading={busy}>
            {busy && busyLabel ? busyLabel : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
