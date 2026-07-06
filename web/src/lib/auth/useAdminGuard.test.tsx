import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';

/**
 * Slice OPS.M.12.7 — `useAdminGuard` returns
 *   'loading' | 'unauthenticated' | 'ok' | 'social-admin-rejected'
 *
 * Truth table for `social-admin-rejected`:
 *   idp is a social provider AND (isPlatformAdmin OR memberships.length > 0)
 */

const useMsalMock = vi.fn();
vi.mock('@azure/msal-react', () => ({ useMsal: () => useMsalMock() }));
vi.mock('@azure/msal-browser', () => ({ InteractionStatus: { None: 'none' } }));

// useAuthedQuery (which useAdminGuard calls) needs useAuth to say the token
// is ready — the guard's own MSAL gate is a redundant belt.
const useAuthMock = vi.fn().mockReturnValue({ isAuthenticated: true, isBusy: false });
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => useAuthMock() }));

const getCurrentUserMock: Mock = vi.fn();
vi.mock('../api/me', () => ({ getCurrentUser: () => getCurrentUserMock() }));

const getMyTenantsMock: Mock = vi.fn();
vi.mock('../tenants/useMyTenants', () => ({ getMyTenants: () => getMyTenantsMock() }));

const withQC = (client: QueryClient) => {
  const Wrapper = ({ children }: { readonly children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

beforeEach(() => {
  useMsalMock.mockReset();
  useAuthMock.mockReset();
  useAuthMock.mockReturnValue({ isAuthenticated: true, isBusy: false });
  getCurrentUserMock.mockReset();
  getMyTenantsMock.mockReset();
});

afterEach(() => {
  vi.clearAllMocks();
});

const buildAccount = (idp: string | null) => ({
  homeAccountId: 'x',
  localAccountId: 'oid-1',
  environment: 'e',
  tenantId: 't',
  username: 'u@example.com',
  idTokenClaims: idp ? { idp } : {},
});

const seedMe = (
  isPlatformAdmin: boolean,
  membershipCount: number,
): void => {
  getCurrentUserMock.mockResolvedValue({
    id: 'u1', email: 'u@example.com', displayName: 'U',
    phone: null, isOwner: false, isAdmin: false,
    isPlatformAdmin, emailVerified: true,
    createdAt: '2026-01-01', lastLoginAt: null,
  });
  getMyTenantsMock.mockResolvedValue({
    memberships: Array.from({ length: membershipCount }, (_, i) => ({
      tenantId: `t-${i}`, displayName: `T${i}`, slug: `t${i}`,
      role: 'tenant_admin', isPrimary: i === 0, status: 'Active',
    })),
    isPlatformAdmin,
  });
};

const runHook = async () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const { useAdminGuard } = await import('./useAdminGuard');
  return renderHook(() => useAdminGuard(), { wrapper: withQC(qc) });
};

describe('useAdminGuard', () => {
  it('returns loading while MSAL interaction is in progress', async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [buildAccount('entra')], inProgress: 'redirect' });
    seedMe(false, 0);
    const { result } = await runHook();
    expect(result.current.status).toBe('loading');
  });

  it('returns unauthenticated when no MSAL account is present', async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [], inProgress: 'none' });
    seedMe(false, 0);
    const { result } = await runHook();
    expect(result.current.status).toBe('unauthenticated');
  });

  it("returns 'ok' for entra-local + platform admin (the standard admin path)", async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [buildAccount(null)], inProgress: 'none' });
    seedMe(true, 0);
    const { result } = await runHook();
    await waitFor(() => expect(result.current.status).toBe('ok'));
  });

  it("returns 'ok' for google guest with no admin authority", async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [buildAccount('google.com')], inProgress: 'none' });
    seedMe(false, 0);
    const { result } = await runHook();
    await waitFor(() => expect(result.current.status).toBe('ok'));
  });

  it("returns 'social-admin-rejected' for google + platform admin", async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [buildAccount('google.com')], inProgress: 'none' });
    seedMe(true, 0);
    const { result } = await runHook();
    await waitFor(() => expect(result.current.status).toBe('social-admin-rejected'));
    expect(result.current.identityProvider).toBe('google.com');
  });

  it("returns 'social-admin-rejected' for facebook + tenant admin membership", async () => {
    useMsalMock.mockReturnValue({ instance: {}, accounts: [buildAccount('facebook.com')], inProgress: 'none' });
    seedMe(false, 1);
    const { result } = await runHook();
    await waitFor(() => expect(result.current.status).toBe('social-admin-rejected'));
    expect(result.current.identityProvider).toBe('facebook.com');
  });
});
