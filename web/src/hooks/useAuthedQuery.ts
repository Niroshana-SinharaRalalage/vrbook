'use client';

import {
  useQuery,
  type QueryFunctionContext,
  type QueryKey,
  type UseQueryOptions,
  type UseQueryResult,
} from '@tanstack/react-query';
import { useAuth } from '@/lib/auth/useAuth';
import { ApiProblemError } from '@/lib/api/client';

/**
 * Slice OPS.M.10.2 F11.7.4.1 — auth-aware react-query wrapper. Standard
 * contract for every component that calls an `[Authorize]` API endpoint.
 *
 * See `docs/OPS_M_10_2_F11_WEB_AUDIT.md` §Part 3 for the design rationale.
 *
 *   - **Defers the query until MSAL is ready** (account is set + no
 *     interaction in progress). Pre-F11.7.4 client components fired
 *     `useEffect → apiFetch` on mount; if MSAL hadn't yet resolved the
 *     active account, `tokenProvider` returned null, `apiFetch` proceeded
 *     with no `Authorization` header, the API 401'd, and the page sat
 *     permanently on "Unauthorized".
 *   - **401 is surfaced WITHOUT retries.** A 401 means the token didn't
 *     reach the server or was invalid; retrying without re-authenticating
 *     just spams the API.
 *   - **403 + 404 are treated as `data === null`** so the "no booking /
 *     no tenant / no review" empty state is a render concern, not an
 *     exception concern. Override via the `treatAs404` array if the
 *     caller has a different distinction in mind.
 *   - **Exposes `tokenReady` + `needsSignIn`** so the caller can render a
 *     `<SignInGate>` for the not-signed-in branch without re-doing the
 *     MSAL state checks.
 */
export const useAuthedQuery = <T,>(
  options: Omit<UseQueryOptions<T | null>, 'retry'> & {
    /**
     * API status codes that should resolve to `data === null` instead of
     * throwing. Defaults to `[403, 404]` (the "not found / not in scope"
     * shape). Pass `[]` to surface every error as `isError`.
     */
    readonly treatAs404?: readonly number[];
  },
): UseQueryResult<T | null> & {
  readonly tokenReady: boolean;
  readonly needsSignIn: boolean;
} => {
  const { isAuthenticated, isBusy } = useAuth();
  const tokenReady = isAuthenticated && !isBusy;
  const treatAs404 = options.treatAs404 ?? [403, 404];

  const inner = options.queryFn as
    | ((ctx: QueryFunctionContext<QueryKey>) => Promise<T | null>)
    | undefined;
  const wrappedQueryFn = async (ctx: QueryFunctionContext<QueryKey>): Promise<T | null> => {
    if (!inner) {
      throw new Error('useAuthedQuery requires a queryFn');
    }
    try {
      return await inner(ctx);
    } catch (err) {
      if (err instanceof ApiProblemError && treatAs404.includes(err.status)) {
        return null;
      }
      throw err;
    }
  };

  const result = useQuery<T | null>({
    ...options,
    queryFn: wrappedQueryFn,
    enabled: tokenReady && (options as { enabled?: boolean }).enabled !== false,
    retry: false,
  });

  return {
    ...result,
    tokenReady,
    needsSignIn: !isAuthenticated && !isBusy,
  };
};
