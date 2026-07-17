'use client';

import { SettingsLayout } from '@/components/settings/SettingsLayout';

export default function SettingsIndexPage() {
  return (
    <SettingsLayout title="Settings">
      <p className="text-sm text-muted-foreground">
        Choose a section to configure. Changes are validated before saving and recorded in each
        section&rsquo;s change history.
      </p>
    </SettingsLayout>
  );
}
