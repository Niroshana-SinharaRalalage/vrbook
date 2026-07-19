export type SettingsScope = 'tenant' | 'platform';

export interface SettingsNavItem {
  readonly href: string;
  readonly label: string;
  readonly scope: SettingsScope;
  /**
   * Whether the section's domain UI is built. The nav only surfaces `ready`
   * sections; placeholder routes stay URL-reachable (admin-only, unlinked) so
   * each domain story (VRB-212..220) simply flips its flag to `true` when it
   * ships its panel. Keeps unbuilt "coming soon" sections out of prod discovery.
   */
  readonly ready: boolean;
}

/**
 * VRB-210 — the settings section registry. Tenant sections live under
 * `/admin/settings/*` (tenant_admin), platform sections under
 * `/admin/platform/settings/*` (platform-admin) per ADR-0016. Only `ready`
 * sections appear in the nav; the rest are scaffolded placeholders their domain
 * story fills (then flips `ready`).
 */
export const SETTINGS_SECTIONS: readonly SettingsNavItem[] = [
  { href: '/admin/settings/cancellation', label: 'Cancellation policy', scope: 'tenant', ready: true },
  { href: '/admin/settings/listing', label: 'Listing details', scope: 'tenant', ready: false },
  { href: '/admin/settings/pricing', label: 'Pricing & fees', scope: 'tenant', ready: false },
  { href: '/admin/settings/availability', label: 'Availability', scope: 'tenant', ready: false },
  { href: '/admin/settings/notifications', label: 'Notifications', scope: 'tenant', ready: false },
  { href: '/admin/platform/settings/cancellation-tiers', label: 'Global cancellation tiers', scope: 'platform', ready: false },
  { href: '/admin/platform/settings/platform-fee', label: 'Platform fee', scope: 'platform', ready: false },
  { href: '/admin/platform/settings/tax-posture', label: 'Tax posture', scope: 'platform', ready: false },
];
