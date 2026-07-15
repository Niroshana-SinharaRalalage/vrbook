import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/api/client', () => ({
  apiFetch: vi.fn().mockResolvedValue({ id: 'u1', displayName: 'Ada', phone: null }),
  // ApiProblemError isn't used here but keep the module shape intact.
  ApiProblemError: class extends Error {},
}));

import { apiFetch } from '@/lib/api/client';
import { updateProfile } from '@/lib/api/me';
import { useUpdateProfile } from './useUpdateProfile';

afterEach(() => vi.clearAllMocks());

describe('updateProfile (api)', () => {
  it('PUTs the body to /api/v1/me', async () => {
    await updateProfile({ displayName: 'Ada', phone: '+1' });
    expect(apiFetch).toHaveBeenCalledWith('/api/v1/me', {
      method: 'PUT',
      body: { displayName: 'Ada', phone: '+1' },
    });
  });
});

describe('useUpdateProfile', () => {
  it('invalidates the ["me"] query on success', async () => {
    const qc = new QueryClient();
    const invalidate = vi.spyOn(qc, 'invalidateQueries');
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    );

    const { result } = renderHook(() => useUpdateProfile(), { wrapper });
    result.current.mutate({ displayName: 'Ada', phone: null });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['me'] });
  });
});
