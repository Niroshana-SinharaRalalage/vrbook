'use client';

import { useConsent } from '@/lib/consent/ConsentProvider';

/**
 * VRB-311 — footer link that re-opens the cookie "Manage" modal, satisfying the
 * "consent choice is re-openable" AC. Styled as a footer link, not a button.
 */
export const CookiePreferencesButton = ({ className }: { readonly className?: string }) => {
  const { openManage } = useConsent();
  return (
    <button type="button" onClick={openManage} className={className}>
      Cookie preferences
    </button>
  );
};
