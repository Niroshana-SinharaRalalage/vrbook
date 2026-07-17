'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { SettingsPlaceholder } from '@/components/settings/SettingsPlaceholder';

export default function Page() {
  return (
    <SettingsLayout title="Pricing & fees">
      <SettingsPlaceholder story="VRB-213" />
    </SettingsLayout>
  );
}
