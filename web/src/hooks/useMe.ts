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
    // Slice OPS.M.13.6 — bounded 1-shot 401 retry lets the MSAL init race
    // self-heal on cold loads (token provider waited for account
    // materialization; second attempt fires with a valid bearer). All other
    // 4xx still fail-fast to avoid masking real auth problems.
    retry: (failureCount, error) => {
      const status = (error as { status?: number } | undefined)?.status;
      return status === 401 && failureCount < 1;
    },
    retryDelay: 400,
  });
