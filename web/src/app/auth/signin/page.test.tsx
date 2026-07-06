import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Slice OPS.M.12.6 — `/auth/signin` MUST:
 *  - Parse `?flow` (defaulting to `guest`), only accepting the two known values.
 *  - Parse `?returnTo` (defaulting to `/`).
 *  - Persist the flow to sessionStorage BEFORE `loginRedirect` so silent
 *    refresh reconstructs the right authority.
 *  - Call `loginRedirect` with `loginRequestFor(flow, returnTo)`.
 */

const loginRedirect = vi.fn().mockResolvedValue(undefined);

let searchParamsMap = new URLSearchParams();

vi.mock('next/navigation', () => ({
  useSearchParams: () => searchParamsMap,
}));

const inProgressRef: { value: string } = { value: 'none' };

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: { loginRedirect },
    inProgress: inProgressRef.value,
  }),
}));

vi.mock('@azure/msal-browser', () => ({
  InteractionStatus: { None: 'none' },
}));

const loginRequestForMock = vi.fn((flow: string, returnTo: string) => ({
  __mock: 'redirect-request',
  flow,
  returnTo,
  authority: `https://example/${flow}`,
}));

vi.mock('../../../lib/auth/msalConfig', () => ({
  loginRequestFor: (flow: string, returnTo: string) => loginRequestForMock(flow, returnTo),
  SIGN_IN_FLOW_STORAGE_KEY: 'vrbook-signin-flow',
}));

beforeEach(() => {
  loginRedirect.mockClear();
  loginRequestForMock.mockClear();
  searchParamsMap = new URLSearchParams();
  inProgressRef.value = 'none';
  window.sessionStorage.clear();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('/auth/signin page', () => {
  it('kicks off loginRedirect with flow=guest by default', async () => {
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(loginRedirect).toHaveBeenCalledTimes(1);
    expect(loginRequestForMock).toHaveBeenCalledWith('guest', '/');
  });

  it('kicks off loginRedirect with flow=admin when ?flow=admin', async () => {
    searchParamsMap = new URLSearchParams({ flow: 'admin', returnTo: '/admin/x' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(loginRedirect).toHaveBeenCalledTimes(1);
    expect(loginRequestForMock).toHaveBeenCalledWith('admin', '/admin/x');
  });

  it('treats an unknown flow value as guest (fail-safe)', async () => {
    searchParamsMap = new URLSearchParams({ flow: 'root', returnTo: '/danger' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(loginRequestForMock).toHaveBeenCalledWith('guest', '/danger');
  });

  it('persists the resolved flow to sessionStorage before redirect', async () => {
    searchParamsMap = new URLSearchParams({ flow: 'admin' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(window.sessionStorage.getItem('vrbook-signin-flow')).toBe('admin');
  });

  it('does not fire loginRedirect while MSAL interaction is in progress', async () => {
    inProgressRef.value = 'redirect';
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(loginRedirect).not.toHaveBeenCalled();
  });

  it('renders a "Redirecting to sign-in…" placeholder', async () => {
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(screen.getByText(/Redirecting to sign-in/i)).toBeInTheDocument();
  });
});
