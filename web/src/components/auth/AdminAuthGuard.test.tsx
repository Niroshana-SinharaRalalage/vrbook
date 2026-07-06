import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Slice OPS.M.12.6 + 12.7 — the admin-subtree guard MUST:
 *  - Redirect unauthenticated visitors to
 *    `/auth/signin?flow=admin&returnTo=<pathname>` (NOT the generic
 *    `signIn()` call). The `?flow=admin` param routes through the
 *    `AdminSignUpSignIn` Entra user flow (no social buttons — ADR-0016).
 *  - Redirect a social-IdP admin to
 *    `/auth/admin-social-idp-rejected?provider=<idp>` (M.12.7).
 *  - Render children only when the user is authenticated AND the
 *    admin-vs-social guard returns 'ok'.
 */

const replaceMock = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: replaceMock, push: vi.fn() }),
  usePathname: () => '/admin/properties/42',
}));

const useAuthMock = vi.fn();
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => useAuthMock(),
}));

const useAdminGuardMock = vi.fn();
vi.mock('@/lib/auth/useAdminGuard', () => ({
  useAdminGuard: () => useAdminGuardMock(),
}));

beforeEach(() => {
  replaceMock.mockClear();
  useAuthMock.mockReset();
  useAdminGuardMock.mockReset();
  // Default: guard returns 'ok' unless overridden.
  useAdminGuardMock.mockReturnValue({ status: 'ok' });
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('<AdminAuthGuard />', () => {
  it('redirects unauthenticated visitors to /auth/signin?flow=admin with returnTo', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: false, isBusy: false });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard>hi</AdminAuthGuard>);
    expect(replaceMock).toHaveBeenCalledWith(
      `/auth/signin?flow=admin&returnTo=${encodeURIComponent('/admin/properties/42')}`,
    );
  });

  it('does not redirect while MSAL interaction is in progress', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: false, isBusy: true });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard>hi</AdminAuthGuard>);
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('renders children when the user is authenticated AND guard = ok', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: true, isBusy: false });
    useAdminGuardMock.mockReturnValue({ status: 'ok' });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard><div data-testid="admin-content">real admin ui</div></AdminAuthGuard>);
    expect(screen.getByTestId('admin-content')).toBeInTheDocument();
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('shows a "Checking sign-in" placeholder while unauthenticated + not yet redirected', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: false, isBusy: false });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard><div>hidden admin content</div></AdminAuthGuard>);
    expect(screen.getByText(/Checking sign-in/i)).toBeInTheDocument();
    expect(screen.queryByText(/hidden admin content/i)).not.toBeInTheDocument();
  });

  it("redirects social-admin-rejected to /auth/admin-social-idp-rejected with the provider", async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: true, isBusy: false });
    useAdminGuardMock.mockReturnValue({ status: 'social-admin-rejected', identityProvider: 'google.com' });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard><div data-testid="admin-content">forbidden</div></AdminAuthGuard>);
    expect(replaceMock).toHaveBeenCalledWith('/auth/admin-social-idp-rejected?provider=google.com');
    expect(screen.queryByTestId('admin-content')).not.toBeInTheDocument();
  });

  it('waits for guard status = loading rather than rendering children', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: true, isBusy: false });
    useAdminGuardMock.mockReturnValue({ status: 'loading' });
    const { AdminAuthGuard } = await import('./AdminAuthGuard');
    render(<AdminAuthGuard><div data-testid="admin-content">not yet</div></AdminAuthGuard>);
    expect(screen.queryByTestId('admin-content')).not.toBeInTheDocument();
    expect(screen.getByText(/Checking sign-in/i)).toBeInTheDocument();
  });
});
