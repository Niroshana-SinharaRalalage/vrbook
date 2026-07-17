import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SettingsAccessGate } from './SettingsAccessGate';

const access = { isPlatformAdmin: false, isTenantAdmin: false, isLoading: false };
vi.mock('./useSettingsAccess', () => ({ useSettingsAccess: () => access }));

describe('<SettingsAccessGate /> (VRB-210, ADR-0016)', () => {
  beforeEach(() => {
    access.isPlatformAdmin = false;
    access.isTenantAdmin = false;
    access.isLoading = false;
  });

  it('renders children for a tenant-admin on a tenant section', () => {
    access.isTenantAdmin = true;
    render(<SettingsAccessGate require="tenant">ok</SettingsAccessGate>);
    expect(screen.getByText('ok')).toBeInTheDocument();
  });

  it('refuses a tenant-admin on a platform section (403, not children)', () => {
    access.isTenantAdmin = true;
    render(<SettingsAccessGate require="platform">secret</SettingsAccessGate>);
    expect(screen.queryByText('secret')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/platform administrators/i);
  });

  it('lets a platform-admin into a tenant section too', () => {
    access.isPlatformAdmin = true;
    render(<SettingsAccessGate require="tenant">ok</SettingsAccessGate>);
    expect(screen.getByText('ok')).toBeInTheDocument();
  });

  it('shows a checking state while loading', () => {
    access.isLoading = true;
    render(<SettingsAccessGate require="tenant">ok</SettingsAccessGate>);
    expect(screen.getByText(/checking access/i)).toBeInTheDocument();
    expect(screen.queryByText('ok')).not.toBeInTheDocument();
  });
});
