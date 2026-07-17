'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Availability">
      <SettingsPlaceholder story="VRB-214" />
    </SettingsLayout>
  );
}
