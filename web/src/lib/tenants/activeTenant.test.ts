/**
 * Slice OPS.M.13.5 — unit tests for the activeTenant helpers.
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
});
