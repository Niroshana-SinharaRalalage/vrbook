'use client';

import { useState } from 'react';

import { RecentChangesPanel } from '@/components/settings/RecentChangesPanel';
import { SettingsLayout } from '@/components/settings/SettingsLayout';
import { CancellationPanel } from '@/components/settings/panels/CancellationPanel';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { adminListMyProperties, type AdminPropertySummary } from '@/lib/api/catalog';

export default function CancellationSettingsPage() {
  const properties = useAuthedQuery<readonly AdminPropertySummary[]>({
    queryKey: ['admin', 'properties'],
    queryFn: adminListMyProperties,
  });
  const [picked, setPicked] = useState('');

  const list = properties.data ?? [];
  const propertyId = picked || list[0]?.id || '';

  return (
    <SettingsLayout title="Cancellation policy">
      <div className="space-y-6">
        {list.length > 0 && (
          <div className="max-w-sm">
            <label htmlFor="cx-property" className="mb-1 block text-xs font-medium text-muted-foreground">
              Listing
            </label>
            <select
              id="cx-property"
              value={propertyId}
              onChange={(e) => setPicked(e.target.value)}
              className="min-h-11 w-full rounded-md border border-input bg-background px-3 text-sm"
            >
              {list.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.title}
                </option>
              ))}
            </select>
          </div>
        )}

        {propertyId ? (
          <>
            <CancellationPanel key={propertyId} propertyId={propertyId} />
            {/* Audit trail renders at the page level so it shows the live
                /admin/settings/changes history independently of the
                cancellation-model endpoint's availability (VRB-211). */}
            <RecentChangesPanel section="cancellation" propertyId={propertyId} />
          </>
        ) : (
          <p className="text-sm text-muted-foreground">
            You have no listings yet. Create one to configure its cancellation policy.
          </p>
        )}
      </div>
    </SettingsLayout>
  );
}
