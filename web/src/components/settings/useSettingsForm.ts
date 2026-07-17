'use client';

import { useCallback, useMemo, useState } from 'react';

import { ApiProblemError } from '@/lib/api/client';

export type FieldErrors<T> = Partial<Record<keyof T, string>>;

export interface UseSettingsFormOptions<T extends object> {
  /** Loaded settings (safe defaults if the server had none). */
  readonly initial: T;
  /** Client-side validation mirroring the server rules. */
  readonly validate?: (values: T) => FieldErrors<T>;
  /** Persist (the PUT). Throws ApiProblemError on 422 with a field-error map. */
  readonly onSave: (values: T) => Promise<unknown>;
  /** Called after a successful save (e.g. invalidate the audit query). */
  readonly onSaved?: () => void;
}

export interface UseSettingsFormResult<T extends object> {
  readonly values: T;
  readonly setValue: <K extends keyof T>(key: K, value: T[K]) => void;
  readonly errors: FieldErrors<T>;
  readonly errorCount: number;
  readonly isDirty: boolean;
  readonly isSaving: boolean;
  readonly savedAt: number | null;
  readonly save: () => Promise<boolean>;
  readonly discard: () => void;
}

/**
 * VRB-210 — the shared settings form engine every settings panel uses. Tracks
 * dirty state, discards back to the last-saved baseline, runs client validation,
 * and maps RFC-7807 `problem.errors` field errors from the server back onto the
 * form. Focus-to-first-error is handled by the SaveBar (which reads
 * `[aria-invalid]`); the DS `Field` wires `aria-invalid`/`aria-describedby` from
 * each field's `error`.
 */
export const useSettingsForm = <T extends object>({
  initial,
  validate,
  onSave,
  onSaved,
}: UseSettingsFormOptions<T>): UseSettingsFormResult<T> => {
  const [values, setValues] = useState<T>(initial);
  // The last-saved snapshot — state (not a ref) so updating it re-renders and
  // `isDirty` recomputes after a save.
  const [baseline, setBaseline] = useState<T>(initial);
  const [errors, setErrors] = useState<FieldErrors<T>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  const setValue = useCallback(<K extends keyof T>(key: K, value: T[K]) => {
    setValues((prev) => ({ ...prev, [key]: value }));
    // Clear a field's error as the user edits it.
    setErrors((prev) => (prev[key] ? { ...prev, [key]: undefined } : prev));
    setSavedAt(null);
  }, []);

  const isDirty = useMemo(
    () => JSON.stringify(values) !== JSON.stringify(baseline),
    [values, baseline],
  );

  const discard = useCallback(() => {
    setValues(baseline);
    setErrors({});
    setSavedAt(null);
  }, [baseline]);

  const save = useCallback(async (): Promise<boolean> => {
    const clientErrors = validate?.(values) ?? {};
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      return false;
    }
    setIsSaving(true);
    setErrors({});
    try {
      await onSave(values);
      setBaseline(values); // new saved baseline → clears dirty
      setSavedAt(Date.now());
      onSaved?.();
      return true;
    } catch (e) {
      if (e instanceof ApiProblemError && e.problem.errors) {
        const mapped: FieldErrors<T> = {};
        for (const [field, messages] of Object.entries(e.problem.errors)) {
          (mapped as Record<string, string>)[field] = messages[0] ?? 'Invalid value.';
        }
        setErrors(mapped);
      } else {
        // Non-field error surfaces as a form-level message on a reserved key.
        (setErrors as (e: FieldErrors<T>) => void)({
          _form: e instanceof Error ? e.message : 'Save failed.',
        } as FieldErrors<T>);
      }
      return false;
    } finally {
      setIsSaving(false);
    }
  }, [values, validate, onSave, onSaved]);

  const errorCount = Object.values(errors).filter(Boolean).length;

  return { values, setValue, errors, errorCount, isDirty, isSaving, savedAt, save, discard };
};
