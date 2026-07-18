import { render, screen, within } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SettingsLayout } from './SettingsLayout';

expect.extend(toHaveNoViolations);

vi.mock('next/navigation', () => ({
  usePathname: () => '/admin/settings/cancellation',
  useRouter: () => ({ push: vi.fn() }),
}));

const access = { isPlatformAdmin: false, isTenantAdmin: true, isLoading: false };
vi.mock('./useSettingsAccess', () => ({ useSettingsAccess: () => access }));

describe('<SettingsLayout /> — role-gated sections (VRB-210, ADR-0016)', () => {
  beforeEach(() => {
    access.isPlatformAdmin = false;
    access.isTenantAdmin = true;
  });

  const nav = () => screen.getByRole('navigation', { name: /settings sections/i });

  it('a tenant-admin sees tenant sections but NOT platform-only sections', () => {
    render(<SettingsLayout title="Settings">body</SettingsLayout>);
    expect(within(nav()).getByRole('link', { name: 'Cancellation policy' })).toBeInTheDocument();
    expect(within(nav()).queryByRole('link', { name: 'Platform fee' })).not.toBeInTheDocument();
    expect(within(nav()).queryByRole('link', { name: 'Global cancellation tiers' })).not.toBeInTheDocument();
  });

  it('a platform-admin sees platform sections', () => {
    access.isPlatformAdmin = true;
    access.isTenantAdmin = false;
    render(<SettingsLayout title="Settings">body</SettingsLayout>);
    expect(within(nav()).getByRole('link', { name: 'Platform fee' })).toBeInTheDocument();
    expect(within(nav()).getByRole('link', { name: 'Tax posture' })).toBeInTheDocument();
    expect(within(nav()).queryByRole('link', { name: 'Cancellation policy' })).not.toBeInTheDocument();
  });

  it('renders the page title', () => {
    render(<SettingsLayout title="My Settings">body</SettingsLayout>);
    expect(screen.getByRole('heading', { level: 1, name: 'My Settings' })).toBeInTheDocument();
  });

  it('has no axe violations (VRB-110-followup)', async () => {
    const { container } = render(<SettingsLayout title="Settings">body</SettingsLayout>);
    expect(await axe(container)).toHaveNoViolations();
  });
});
