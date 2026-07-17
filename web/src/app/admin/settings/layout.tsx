'use client';

import { SettingsAccessGate } from '@/components/settings/SettingsAccessGate';

export default function TenantSettingsLayout({ children }: { readonly children: React.ReactNode }) {
  return <SettingsAccessGate require="tenant">{children}</SettingsAccessGate>;
}
