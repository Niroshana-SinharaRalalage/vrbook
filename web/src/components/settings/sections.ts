export type SettingsScope = 'tenant' | 'platform';

export interface SettingsNavItem {
  readonly href: string;
  readonly label: string;
  readonly scope: SettingsScope;
}

/**
 * VRB-210 — the settings section registry. Tenant sections live under
 * `/admin/settings/*` (tenant_admin), platform sections under
 * `/admin/platform/settings/*` (platform-admin) per ADR-0016. Most are
 * scaffolded placeholders until their domain story (VRB-212..220) fills them;
 * `cancellation` (tenant) is the wired worked example.
 */
export const SETTINGS_SECTIONS: readonly SettingsNavItem[] = [
  { href: '/admin/settings/cancellation', label: 'Cancellation policy', scope: 'tenant' },
  { href: '/admin/settings/listing', label: 'Listing details', scope: 'tenant' },
  { href: '/admin/settings/pricing', label: 'Pricing & fees', scope: 'tenant' },
  { href: '/admin/settings/availability', label: 'Availability', scope: 'tenant' },
  { href: '/admin/settings/notifications', label: 'Notifications', scope: 'tenant' },
  { href: '/admin/platform/settings/cancellation-tiers', label: 'Global cancellation tiers', scope: 'platform' },
  { href: '/admin/platform/settings/platform-fee', label: 'Platform fee', scope: 'platform' },
  { href: '/admin/platform/settings/tax-posture', label: 'Tax posture', scope: 'platform' },
];
