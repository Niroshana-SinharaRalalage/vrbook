'use client';

import { SettingsAccessGate } from '@/components/settings/SettingsAccessGate';

export default function PlatformSettingsLayout({ children }: { readonly children: React.ReactNode }) {
  return <SettingsAccessGate require="platform">{children}</SettingsAccessGate>;
}
