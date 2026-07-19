'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { GlobalTiersPanel } from '@/components/settings/panels/GlobalTiersPanel';

export default function Page() {
  return (
    <SettingsLayout title="Global cancellation tiers">
      <GlobalTiersPanel />
    </SettingsLayout>
  );
}
