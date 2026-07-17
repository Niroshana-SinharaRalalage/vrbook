'use client';

import { AlertCircle, Check } from 'lucide-react';

import { Button } from '@/components/ui';

/**
 * VRB-210 — sticky save/discard bar for every settings panel. Shows unsaved /
 * error-count / saved states, and on a failed save moves focus to the first
 * invalid field (the DS `Field` marks it `aria-invalid`).
 */
export const SaveBar = ({
  isDirty,
  errorCount,
  isSaving,
  savedAt,
  onSave,
  onDiscard,
}: {
  readonly isDirty: boolean;
  readonly errorCount: number;
  readonly isSaving: boolean;
  readonly savedAt: number | null;
  readonly onSave: () => Promise<boolean>;
  readonly onDiscard: () => void;
}) => {
  const handleSave = async () => {
    const ok = await onSave();
    if (!ok) {
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
    }
  };

  if (!isDirty && savedAt === null && errorCount === 0) return null;

  return (
    <div
      role="region"
      aria-label="Save changes"
      className="sticky bottom-0 z-30 mt-6 flex items-center justify-between gap-3 border-t border-border bg-background/95 py-3 backdrop-blur"
    >
      <div className="text-sm">
        {errorCount > 0 ? (
          <span className="inline-flex items-center gap-1 text-destructive">
            <AlertCircle className="h-4 w-4" aria-hidden="true" />
            {errorCount} {errorCount === 1 ? 'error' : 'errors'} to fix
          </span>
        ) : isDirty ? (
          <span className="text-muted-foreground">Unsaved changes</span>
        ) : savedAt ? (
          <span role="status" className="inline-flex items-center gap-1 text-green-700 dark:text-green-400">
            <Check className="h-4 w-4" aria-hidden="true" /> Saved
          </span>
        ) : null}
      </div>
      <div className="flex gap-2">
        <Button variant="outline" size="sm" onClick={onDiscard} disabled={!isDirty || isSaving}>
          Discard
        </Button>
        <Button variant="primary" size="sm" onClick={handleSave} loading={isSaving} disabled={!isDirty}>
          Save changes
        </Button>
      </div>
    </div>
  );
};
