'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';

import { updateProfile, type UpdateProfileBody } from '@/lib/api/me';

/**
 * VRB-108 — `PUT /api/v1/me` mutation. On success it invalidates the `['me']`
 * query so `useMe` (and everything keyed on it — header, nav) re-reads the
 * updated profile.
 */
export const useUpdateProfile = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateProfileBody) => updateProfile(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['me'] }),
  });
};
