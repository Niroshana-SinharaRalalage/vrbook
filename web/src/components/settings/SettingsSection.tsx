import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui';

/**
 * VRB-210 — a titled card grouping related settings fields inside a panel.
 */
export const SettingsSection = ({
  title,
  description,
  children,
}: {
  readonly title: string;
  readonly description?: React.ReactNode;
  readonly children: React.ReactNode;
}) => (
  <Card>
    <CardHeader>
      <CardTitle className="text-base">{title}</CardTitle>
      {description && <CardDescription>{description}</CardDescription>}
    </CardHeader>
    <CardContent className="space-y-4">{children}</CardContent>
  </Card>
);
