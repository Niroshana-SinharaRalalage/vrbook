import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { axe, toHaveNoViolations } from 'jest-axe';

import { ApiProblemError } from '@/lib/api/client';

expect.extend(toHaveNoViolations);

const useMeMock = vi.fn();
const authMock = { isAuthenticated: true, signIn: vi.fn() };
let mutationMock: {
  mutateAsync: ReturnType<typeof vi.fn>;
  isPending: boolean;
  isError: boolean;
  error: unknown;
};

vi.mock('@/hooks/useMe', () => ({ useMe: () => useMeMock() }));
vi.mock('@/hooks/useUpdateProfile', () => ({ useUpdateProfile: () => mutationMock }));
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => authMock }));
vi.mock('@/lib/api/loyalty', () => ({ getMyLoyalty: () => Promise.resolve({ tier: 'Gold' }) }));

import { ProfileForm } from './ProfileForm';

const me = {
  id: 'u1',
  email: 'guest@example.com',
  displayName: 'Ada Guest',
  phone: '+1 555 0100',
  isOwner: false,
  isAdmin: false,
  isPlatformAdmin: false,
  emailVerified: true,
  createdAt: '2026-01-01T00:00:00Z',
  lastLoginAt: null,
};

const renderForm = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <ProfileForm />
    </QueryClientProvider>,
  );
};

beforeEach(() => {
  useMeMock.mockReset();
  useMeMock.mockReturnValue({ data: me, isLoading: false });
  authMock.isAuthenticated = true;
  mutationMock = { mutateAsync: vi.fn().mockResolvedValue(me), isPending: false, isError: false, error: null };
});
afterEach(() => vi.clearAllMocks());

describe('<ProfileForm />', () => {
  it('prompts sign-in when the guest is not authenticated', () => {
    authMock.isAuthenticated = false;
    renderForm();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
    expect(screen.queryByLabelText(/display name/i)).not.toBeInTheDocument();
  });

  it('populates the form from useMe with a read-only email', () => {
    renderForm();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('Ada Guest');
    expect(screen.getByLabelText(/phone/i)).toHaveValue('+1 555 0100');
    const email = screen.getByLabelText(/email/i);
    expect(email).toHaveValue('guest@example.com');
    expect(email).toHaveAttribute('readonly');
  });

  it('disables Save until a field changes (pristine)', () => {
    renderForm();
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('submits displayName + phone to the mutation and confirms saved', async () => {
    const user = userEvent.setup();
    renderForm();
    const name = screen.getByLabelText(/display name/i);
    await user.clear(name);
    await user.type(name, 'Ada Lovelace');
    await user.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(mutationMock.mutateAsync).toHaveBeenCalledWith({
        displayName: 'Ada Lovelace',
        phone: '+1 555 0100',
      }),
    );
    expect(await screen.findByText(/saved/i)).toBeInTheDocument();
  });

  it('blocks submit and shows a field error when displayName is emptied', async () => {
    const user = userEvent.setup();
    renderForm();
    await user.clear(screen.getByLabelText(/display name/i));
    await user.click(screen.getByRole('button', { name: /save/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/name/i);
    expect(mutationMock.mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/display name/i)).toHaveAttribute('aria-invalid', 'true');
  });

  it('disables the inputs and busies the button while the save is in flight', () => {
    mutationMock.isPending = true;
    renderForm();
    const save = screen.getByRole('button', { name: /save/i });
    expect(save).toBeDisabled();
    expect(save).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByLabelText(/display name/i)).toBeDisabled();
  });

  it('surfaces a form-level error when the server rejects the save', () => {
    mutationMock.isError = true;
    mutationMock.error = new ApiProblemError(422, { status: 422, detail: 'Phone number is invalid.' });
    renderForm();
    expect(screen.getByRole('alert')).toHaveTextContent('Phone number is invalid.');
  });

  it('shows the loyalty tier as a badge', async () => {
    renderForm();
    expect(await screen.findByText('Gold')).toBeInTheDocument();
  });

  it('has no axe violations (VRB-110)', async () => {
    const { container } = renderForm();
    await screen.findByLabelText(/display name/i);
    expect(await axe(container)).toHaveNoViolations();
  });
});
