import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ApiProblemError } from '@/lib/api/client';
import { useSettingsForm } from './useSettingsForm';

interface Form {
  name: string;
  max: number;
}

const setup = (over: Partial<Parameters<typeof useSettingsForm<Form>>[0]> = {}) =>
  renderHook(() =>
    useSettingsForm<Form>({
      initial: { name: 'A', max: 4 },
      onSave: vi.fn().mockResolvedValue(undefined),
      ...over,
    }),
  );

describe('useSettingsForm (VRB-210)', () => {
  it('tracks dirty state and discards back to baseline', () => {
    const { result } = setup();
    expect(result.current.isDirty).toBe(false);
    act(() => result.current.setValue('name', 'B'));
    expect(result.current.isDirty).toBe(true);
    act(() => result.current.discard());
    expect(result.current.values.name).toBe('A');
    expect(result.current.isDirty).toBe(false);
  });

  it('blocks save on client validation errors and reports the count', async () => {
    const onSave = vi.fn();
    const { result } = setup({
      validate: (v) => (v.name ? {} : { name: 'Required' }),
      onSave,
    });
    act(() => result.current.setValue('name', ''));
    let ok: boolean | undefined;
    await act(async () => {
      ok = await result.current.save();
    });
    expect(ok).toBe(false);
    expect(onSave).not.toHaveBeenCalled();
    expect(result.current.errors.name).toBe('Required');
    expect(result.current.errorCount).toBe(1);
  });

  it('clears a field error as the user edits it', () => {
    const { result } = setup({ validate: () => ({ name: 'Required' }) });
    act(() => {
      void result.current.save();
    });
    act(() => result.current.setValue('name', 'X'));
    expect(result.current.errors.name).toBeUndefined();
  });

  it('saves, updates the baseline (clears dirty) and stamps savedAt', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const onSaved = vi.fn();
    const { result } = setup({ onSave, onSaved });
    act(() => result.current.setValue('max', 6));
    await act(async () => {
      await result.current.save();
    });
    expect(onSave).toHaveBeenCalledWith({ name: 'A', max: 6 });
    expect(result.current.isDirty).toBe(false);
    expect(result.current.savedAt).toBeGreaterThan(0);
    expect(onSaved).toHaveBeenCalled();
  });

  it('maps server RFC-7807 field errors back onto the form', async () => {
    const onSave = vi.fn().mockRejectedValue(
      new ApiProblemError(422, { errors: { max: ['Must be <= 4.'] } }),
    );
    const { result } = setup({ onSave });
    act(() => result.current.setValue('max', 99));
    await act(async () => {
      await result.current.save();
    });
    await waitFor(() => expect(result.current.errors.max).toBe('Must be <= 4.'));
    expect(result.current.isDirty).toBe(true); // unsaved
  });
});
