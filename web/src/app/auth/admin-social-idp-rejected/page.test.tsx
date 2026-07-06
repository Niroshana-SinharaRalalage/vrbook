import { render, screen, fireEvent } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Slice OPS.M.12.7 — the rejection error page renders the correct copy per
 * identity provider and its "Sign out and try again" CTA calls `signOut`.
 */

let searchParamsMap = new URLSearchParams();
vi.mock('next/navigation', () => ({
  useSearchParams: () => searchParamsMap,
}));

const signOutMock = vi.fn();
vi.mock('../../../lib/auth/useAuth', () => ({
  useAuth: () => ({ signOut: signOutMock, isAuthenticated: true, isBusy: false, user: null, signIn: vi.fn(), getAccessToken: vi.fn() }),
}));

beforeEach(() => {
  searchParamsMap = new URLSearchParams();
  signOutMock.mockClear();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('/auth/admin-social-idp-rejected page', () => {
  it('renders the generic title', async () => {
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(
      screen.getByRole('heading', { name: /Admin sign-in requires a workspace account/i }),
    ).toBeInTheDocument();
  });

  it("names the provider when ?provider=google.com is set", async () => {
    searchParamsMap = new URLSearchParams({ provider: 'google.com' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(screen.getByText(/You signed in with Google/i)).toBeInTheDocument();
  });

  it("names Facebook when ?provider=facebook.com", async () => {
    searchParamsMap = new URLSearchParams({ provider: 'facebook.com' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(screen.getByText(/You signed in with Facebook/i)).toBeInTheDocument();
  });

  it('falls back to generic copy on an unrecognised provider value', async () => {
    searchParamsMap = new URLSearchParams({ provider: 'linkedin.com' });
    const Page = (await import('./page')).default;
    render(<Page />);
    expect(screen.getByText(/Social sign-in is available for guest use only/i)).toBeInTheDocument();
    expect(screen.queryByText(/You signed in with LinkedIn/i)).not.toBeInTheDocument();
  });

  it('sign-out CTA calls the useAuth signOut function', async () => {
    const Page = (await import('./page')).default;
    render(<Page />);
    fireEvent.click(screen.getByRole('button', { name: /Sign out and try again/i }));
    expect(signOutMock).toHaveBeenCalledTimes(1);
  });
});
