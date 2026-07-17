'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Global cancellation tiers">
      <SettingsPlaceholder story="VRB-216" />
    </SettingsLayout>
  );
}
