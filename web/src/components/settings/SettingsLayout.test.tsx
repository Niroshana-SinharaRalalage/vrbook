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

  it('a tenant-admin sees only READY tenant sections (placeholders + platform hidden)', () => {
    render(<SettingsLayout title="Settings">body</SettingsLayout>);
    // Cancellation is ready → linked.
    expect(within(nav()).getByRole('link', { name: 'Cancellation policy' })).toBeInTheDocument();
    // A not-yet-built tenant section (ready:false) is hidden from the nav.
    expect(within(nav()).queryByRole('link', { name: 'Pricing & fees' })).not.toBeInTheDocument();
    // Platform-only section hidden for a tenant-admin.
    expect(within(nav()).queryByRole('link', { name: 'Platform fee' })).not.toBeInTheDocument();
  });

  it('hides unbuilt platform sections even from a platform-admin (ready-gated)', () => {
    access.isPlatformAdmin = true;
    access.isTenantAdmin = false;
    render(<SettingsLayout title="Settings">body</SettingsLayout>);
    // No platform section is ready yet → none surface; the domain story flips
    // its `ready` flag when it ships.
    expect(within(nav()).queryByRole('link', { name: 'Platform fee' })).not.toBeInTheDocument();
    expect(within(nav()).queryByRole('link', { name: 'Tax posture' })).not.toBeInTheDocument();
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
