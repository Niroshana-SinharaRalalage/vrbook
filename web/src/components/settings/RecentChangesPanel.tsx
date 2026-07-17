'use client';

import { Card, CardContent, CardHeader, CardTitle, Skeleton } from '@/components/ui';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { getSettingsChanges, type SettingsChangeDto } from '@/lib/api/settings';

/**
 * VRB-210 — "Recent changes" audit panel (who / what / when) sourced from the
 * VRB-211 audit trail (`GET /admin/settings/changes?section=&propertyId=`). Until
 * Agent 2's endpoint lands it degrades to the empty/unavailable state — never
 * blocks the panel it sits beside.
 */
export const RecentChangesPanel = ({
  section,
  propertyId,
}: {
  readonly section: string;
  readonly propertyId?: string;
}) => {
  const q = useAuthedQuery<readonly SettingsChangeDto[]>({
    queryKey: ['settings', 'changes', section, propertyId ?? null],
    queryFn: () => getSettingsChanges(section, propertyId),
  });

  const changes = q.data ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Recent changes</CardTitle>
      </CardHeader>
      <CardContent>
        {q.isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-3/4" />
          </div>
        ) : q.isError ? (
          <p className="text-sm text-muted-foreground">Change history is currently unavailable.</p>
        ) : changes.length === 0 ? (
          <p className="text-sm text-muted-foreground">No changes recorded yet.</p>
        ) : (
          <ul className="space-y-3">
            {changes.map((c, i) => (
              <li key={`${c.action}-${c.at}-${i}`} className="text-sm">
                <span className="font-medium">{c.actor}</span>{' '}
                <span className="text-muted-foreground">changed {c.action.replace(/^settings\./, '')}</span>
                <div className="text-xs text-muted-foreground">{new Date(c.at).toLocaleString()}</div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
};
