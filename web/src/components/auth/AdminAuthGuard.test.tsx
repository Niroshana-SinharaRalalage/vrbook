import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Slice OPS.M.12.6 — the admin-subtree guard MUST redirect unauthenticated
 * visitors to `/auth/signin?flow=admin&returnTo=<pathname>` (not the generic
 * `signIn()` call). The `?flow=admin` param is what routes through the
 * `AdminSignUpSignIn` Entra user flow (no social buttons — see ADR-0016).
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

beforeEach(() => {
  replaceMock.mockClear();
  useAuthMock.mockReset();
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

  it('renders children when the user is authenticated', async () => {
    useAuthMock.mockReturnValue({ isAuthenticated: true, isBusy: false });
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
});
