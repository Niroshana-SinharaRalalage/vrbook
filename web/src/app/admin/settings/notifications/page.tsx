'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Notifications">
      <SettingsPlaceholder story="VRB-218" />
    </SettingsLayout>
  );
}
