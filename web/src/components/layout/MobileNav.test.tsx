import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { axe, toHaveNoViolations } from 'jest-axe';

expect.extend(toHaveNoViolations);

// Radix (via ui/Sheet) touches pointer-capture / scrollIntoView, absent in jsdom.
beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
});

const useMeMock = vi.fn();
const useMyTenantsMock = vi.fn();

vi.mock('@/hooks/useMe', () => ({ useMe: () => useMeMock() }));
vi.mock('@/lib/tenants/useMyTenants', () => ({ useMyTenants: () => useMyTenantsMock() }));
// Child widgets have their own tests + auth/MSAL deps — stub them here.
vi.mock('./SiteHeaderAuth', () => ({ SiteHeaderAuth: () => <button type="button">Sign in</button> }));
vi.mock('./SiteHeaderRoleBadge', () => ({ SiteHeaderRoleBadge: () => <div data-testid="role-badge" /> }));

const meWith = (isPlatformAdmin: boolean) => ({
  data: { id: 'u1', email: 'x@y.z', displayName: 'X', isPlatformAdmin, emailVerified: true },
});
const noMe = () => ({ data: undefined });
const tenantsWith = (memberships: Array<{ role: string }>) => ({
  data: {
    memberships: memberships.map((m, i) => ({
      tenantId: `t${i}`,
      slug: `s${i}`,
      status: 'Active' as const,
      role: m.role,
      isPrimary: i === 0,
      displayName: `Tenant ${i}`,
    })),
    isPlatformAdmin: false,
  },
});
const noTenants = () => ({ data: undefined });

// Returns the trigger element too: once the sheet is open Radix marks the
// background (the hamburger included) aria-hidden, so it's no longer findable
// via getByRole — hold the reference from before opening.
const open = async () => {
  const user = userEvent.setup();
  const trigger = screen.getByRole('button', { name: /menu/i });
  await user.click(trigger);
  return { user, trigger };
};

beforeEach(() => {
  useMeMock.mockReset();
  useMyTenantsMock.mockReset();
  useMeMock.mockReturnValue(noMe());
  useMyTenantsMock.mockReturnValue(noTenants());
});
afterEach(() => vi.clearAllMocks());

describe('<MobileNav />', () => {
  it('renders a collapsed hamburger labelled Menu', async () => {
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    const btn = screen.getByRole('button', { name: /menu/i });
    expect(btn).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('is mobile-only (md:hidden)', async () => {
    const { MobileNav } = await import('./MobileNav');
    const { container } = render(<MobileNav />);
    expect(container.firstElementChild?.className).toContain('md:hidden');
  });

  it('opens the drawer and shows the base guest links', async () => {
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    const { trigger } = await open();
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const dialog = screen.getByRole('dialog');
    for (const label of ['Stays', 'My trips', 'Account']) {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument();
    }
    expect(dialog).toBeInTheDocument();
  });

  it('shows the Admin link for a platform admin', async () => {
    useMeMock.mockReturnValue(meWith(true));
    useMyTenantsMock.mockReturnValue(noTenants());
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    await open();
    expect(screen.getByRole('link', { name: /admin/i })).toHaveAttribute('href', '/admin');
  });

  it('shows the Admin link for a tenant_admin membership', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(tenantsWith([{ role: 'tenant_admin' }]));
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    await open();
    expect(screen.getByRole('link', { name: /admin/i })).toBeInTheDocument();
  });

  it('hides the Admin link for a regular guest', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(tenantsWith([{ role: 'tenant_member' }]));
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    await open();
    expect(screen.queryByRole('link', { name: /admin/i })).not.toBeInTheDocument();
  });

  it('gives nav links a >=44px touch target', async () => {
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    await open();
    expect(screen.getByRole('link', { name: 'Stays' }).className).toContain('min-h-11');
  });

  it('closes and returns focus to the hamburger on Escape', async () => {
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    const { user, trigger } = await open();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.activeElement).toBe(trigger);
  });

  it('closes when a nav link is selected', async () => {
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    const { user } = await open();
    await user.click(screen.getByRole('link', { name: 'Stays' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('open drawer has no axe violations (VRB-110)', async () => {
    useMeMock.mockReturnValue(meWith(true));
    useMyTenantsMock.mockReturnValue(noTenants());
    const { MobileNav } = await import('./MobileNav');
    render(<MobileNav />);
    await open();
    expect(await axe(screen.getByRole('dialog'))).toHaveNoViolations();
  });
});
