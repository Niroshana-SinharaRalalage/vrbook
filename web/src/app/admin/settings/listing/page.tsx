'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Listing details">
      <SettingsPlaceholder story="VRB-212" />
    </SettingsLayout>
  );
}
