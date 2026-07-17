'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Tax posture">
      <SettingsPlaceholder story="VRB-216" />
    </SettingsLayout>
  );
}
