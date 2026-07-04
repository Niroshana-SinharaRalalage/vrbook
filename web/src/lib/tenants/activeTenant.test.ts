/**
 * Slice OPS.M.13.5 — unit tests for the activeTenant helpers.
 * Slice OPS.M.13.7 — extended for sign-out clear-on-mount coverage.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  clearActiveTenantId,
  getActiveTenantId,
  setActiveTenantId,
} from './activeTenant';

describe('activeTenant', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  afterEach(() => {
    window.sessionStorage.clear();
  });

  it('returns null when nothing has been stored', () => {
    expect(getActiveTenantId()).toBeNull();
  });

  it('round-trips a tenant id via sessionStorage', () => {
    setActiveTenantId('11111111-1111-1111-1111-111111111111');
    expect(getActiveTenantId()).toBe('11111111-1111-1111-1111-111111111111');
  });

  it('clears the stored value', () => {
    setActiveTenantId('22222222-2222-2222-2222-222222222222');
    clearActiveTenantId();
    expect(getActiveTenantId()).toBeNull();
  });

  it('setActiveTenantId overwrites an existing value', () => {
    setActiveTenantId('11111111-1111-1111-1111-111111111111');
    setActiveTenantId('22222222-2222-2222-2222-222222222222');
    expect(getActiveTenantId()).toBe('22222222-2222-2222-2222-222222222222');
  });

  it('clearActiveTenantId is idempotent when nothing is stored', () => {
    clearActiveTenantId();
    clearActiveTenantId();
    expect(getActiveTenantId()).toBeNull();
  });

  it('clearActiveTenantId is safe if sessionStorage throws', () => {
    // Simulate privacy mode / disabled storage — the helpers try/catch
    // and silently no-op. Regression sentinel for M.13.7 sign-out flow
    // where a throwing clear would crash the redirect.
    const original = window.sessionStorage.removeItem;
    (window.sessionStorage as unknown as { removeItem: () => void }).removeItem =
      () => {
        throw new Error('QuotaExceededError');
      };
    expect(() => clearActiveTenantId()).not.toThrow();
    window.sessionStorage.removeItem = original;
  });
});
