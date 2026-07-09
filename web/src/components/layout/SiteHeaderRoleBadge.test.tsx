import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Header role-badge tests. The chip surfaces the signed-in user's
 * operator role at a glance so a PlatformAdmin session doesn't look
 * indistinguishable from a TenantAdmin session (both previously showed
 * only a generic "Admin" nav link).
 *
 * <p>Ordering invariant: PlatformAdmin trumps any tenant_admin
 * membership. Data sources match {@link SiteHeaderNav} (post-M.21
 * ADR-0014 shape) — {@link useMe}.isPlatformAdmin AND
 * {@link useMyTenants} memberships filtered on role.</p>
 */

const useMeMock = vi.fn();
const useMyTenantsMock = vi.fn();

vi.mock('@/hooks/useMe', () => ({
  useMe: () => useMeMock(),
}));

vi.mock('@/lib/tenants/useMyTenants', () => ({
  useMyTenants: () => useMyTenantsMock(),
}));

const meWith = (isPlatformAdmin: boolean) => ({
  data: { id: 'u1', email: 'x@y.z', displayName: 'X', isPlatformAdmin, emailVerified: true },
});
const noMe = () => ({ data: undefined });
const tenantsWith = (
  memberships: Array<{ role: string; displayName: string; isPrimary: boolean }>,
) => ({
  data: {
    memberships: memberships.map((m, i) => ({
      tenantId: `t${i}`,
      slug: `slug-${i}`,
      status: 'Active' as const,
      role: m.role,
      isPrimary: m.isPrimary,
      displayName: m.displayName,
    })),
    isPlatformAdmin: false,
  },
});
const noTenants = () => ({ data: undefined });

beforeEach(() => {
  useMeMock.mockReset();
  useMyTenantsMock.mockReset();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('<SiteHeaderRoleBadge />', () => {
  it('renders the PlatformAdmin chip when isPlatformAdmin=true', async () => {
    useMeMock.mockReturnValue(meWith(true));
    useMyTenantsMock.mockReturnValue(noTenants());
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    render(<SiteHeaderRoleBadge />);
    expect(screen.getByTestId('role-badge-platform-admin')).toHaveTextContent(/Platform Admin/i);
    expect(screen.queryByTestId('role-badge-tenant-admin')).not.toBeInTheDocument();
  });

  it('renders the TenantAdmin chip when the user has a tenant_admin membership and is NOT a PlatformAdmin', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(
      tenantsWith([{ role: 'tenant_admin', displayName: 'VrBook Default', isPrimary: true }]),
    );
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    render(<SiteHeaderRoleBadge />);
    expect(screen.getByTestId('role-badge-tenant-admin')).toHaveTextContent(
      /Tenant Admin.*VrBook Default/i,
    );
    expect(screen.queryByTestId('role-badge-platform-admin')).not.toBeInTheDocument();
  });

  it('prefers PlatformAdmin over any tenant_admin membership when both are present', async () => {
    useMeMock.mockReturnValue(meWith(true));
    useMyTenantsMock.mockReturnValue(
      tenantsWith([{ role: 'tenant_admin', displayName: 'Should Not Appear', isPrimary: true }]),
    );
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    render(<SiteHeaderRoleBadge />);
    expect(screen.getByTestId('role-badge-platform-admin')).toBeInTheDocument();
    expect(screen.queryByTestId('role-badge-tenant-admin')).not.toBeInTheDocument();
    expect(screen.queryByText(/Should Not Appear/i)).not.toBeInTheDocument();
  });

  it('picks the isPrimary tenant_admin membership when multiple tenant_admin memberships exist', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(
      tenantsWith([
        { role: 'tenant_admin', displayName: 'Non-Primary Tenant', isPrimary: false },
        { role: 'tenant_admin', displayName: 'Primary Tenant', isPrimary: true },
      ]),
    );
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    render(<SiteHeaderRoleBadge />);
    expect(screen.getByTestId('role-badge-tenant-admin')).toHaveTextContent(/Primary Tenant/);
    expect(screen.queryByText(/Non-Primary Tenant/)).not.toBeInTheDocument();
  });

  it('falls back to the first tenant_admin membership when none are marked primary', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(
      tenantsWith([
        { role: 'tenant_admin', displayName: 'First Tenant', isPrimary: false },
        { role: 'tenant_admin', displayName: 'Second Tenant', isPrimary: false },
      ]),
    );
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    render(<SiteHeaderRoleBadge />);
    expect(screen.getByTestId('role-badge-tenant-admin')).toHaveTextContent(/First Tenant/);
  });

  it('renders nothing for a regular guest (no PA, no tenant_admin membership)', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(tenantsWith([]));
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    const { container } = render(<SiteHeaderRoleBadge />);
    expect(container).toBeEmptyDOMElement();
  });

  it('ignores non-tenant_admin memberships (tenant_member reserved shape)', async () => {
    useMeMock.mockReturnValue(meWith(false));
    useMyTenantsMock.mockReturnValue(
      tenantsWith([{ role: 'tenant_member', displayName: 'Some Tenant', isPrimary: true }]),
    );
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    const { container } = render(<SiteHeaderRoleBadge />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing while both queries are still loading (data undefined)', async () => {
    useMeMock.mockReturnValue(noMe());
    useMyTenantsMock.mockReturnValue(noTenants());
    const { SiteHeaderRoleBadge } = await import('./SiteHeaderRoleBadge');
    const { container } = render(<SiteHeaderRoleBadge />);
    expect(container).toBeEmptyDOMElement();
  });
});
