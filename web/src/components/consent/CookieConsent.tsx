'use client';

import { useEffect, useState } from 'react';

import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui';
import { useConsent } from '@/lib/consent/ConsentProvider';

/**
 * VRB-311 — cookie-consent surface: a non-blocking bottom banner shown on first
 * visit, plus a "Manage" modal (design-system `Dialog`, so it inherits the
 * focus-trap / Esc / focus-return contract — resolves gap G33). Both are always
 * mounted so the footer "Cookie preferences" link can re-open Manage after a
 * choice is made. Analytics scripts load only after consent (enforced in the
 * analytics module); this is purely the UI.
 */
export const CookieConsent = () => {
  const { ready, decided, manageOpen, consent, acceptAll, rejectNonEssential, setAnalytics, openManage, closeManage } =
    useConsent();
  const [analyticsOn, setAnalyticsOn] = useState(false);

  // Seed the Manage toggle from the current choice each time it opens.
  useEffect(() => {
    if (manageOpen) setAnalyticsOn(consent?.analytics === true);
  }, [manageOpen, consent]);

  return (
    <>
      {ready && !decided && (
        <div
          role="region"
          aria-label="Cookie consent"
          className="fixed inset-x-0 bottom-0 z-50 border-t border-border bg-background/95 backdrop-blur motion-safe:animate-in motion-safe:slide-in-from-bottom-4"
        >
          <div className="container flex flex-col gap-3 py-4 text-sm md:flex-row md:items-center md:justify-between">
            <p className="text-muted-foreground">
              We use necessary cookies to run VrBook, and — only with your consent — analytics cookies to
              improve it. See our{' '}
              <a href="/legal/privacy" className="underline underline-offset-2 hover:text-foreground">
                Privacy Policy
              </a>
              .
            </p>
            <div className="flex flex-shrink-0 flex-wrap gap-2">
              <Button variant="ghost" size="sm" onClick={openManage}>
                Manage
              </Button>
              <Button variant="outline" size="sm" onClick={rejectNonEssential}>
                Reject non-essential
              </Button>
              <Button variant="primary" size="sm" onClick={acceptAll}>
                Accept all
              </Button>
            </div>
          </div>
        </div>
      )}

      <Dialog open={manageOpen} onOpenChange={(o) => (o ? openManage() : closeManage())}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Cookie preferences</DialogTitle>
            <DialogDescription>
              Choose which cookies VrBook may use. Necessary cookies are always on.
            </DialogDescription>
          </DialogHeader>

          <ul className="space-y-3">
            <li className="flex items-start justify-between gap-4 rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-medium">Necessary</p>
                <p className="text-xs text-muted-foreground">
                  Required for sign-in, security, and booking. Always active.
                </p>
              </div>
              <input type="checkbox" checked disabled aria-label="Necessary cookies (always on)" className="mt-1 h-4 w-4" />
            </li>
            <li className="flex items-start justify-between gap-4 rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-medium">Analytics</p>
                <p className="text-xs text-muted-foreground">
                  Helps us understand usage to improve the product. Loads only if enabled.
                </p>
              </div>
              <input
                type="checkbox"
                checked={analyticsOn}
                onChange={(e) => setAnalyticsOn(e.target.checked)}
                aria-label="Analytics cookies"
                className="mt-1 h-4 w-4"
              />
            </li>
          </ul>

          <DialogFooter>
            <Button variant="outline" onClick={() => setAnalytics(false)}>
              Reject non-essential
            </Button>
            <Button variant="primary" onClick={() => setAnalytics(analyticsOn)}>
              Save preferences
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
};
