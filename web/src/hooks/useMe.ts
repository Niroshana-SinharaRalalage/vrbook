/**
 * Slice OPS.M.8 §3.7 Step 10 — react-query wrapper around `GET /api/v1/me`.
 * Used by the AdminSidebar to conditionally render the Platform nav group
 * based on `isPlatformAdmin`, and by future feature gates.
 */
import { useQuery } from '@tanstack/react-query';
import { getCurrentUser, type CurrentUser } from '@/lib/api/me';

export const useMe = () =>
  useQuery<CurrentUser>({
    queryKey: ['me'],
    queryFn: getCurrentUser,
    staleTime: 60_000,
    retry: false,
  });
