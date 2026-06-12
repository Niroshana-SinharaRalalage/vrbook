'use client';

import { useEffect, useState } from 'react';
import { UserCircle, ChevronDown, Check } from 'lucide-react';

import {
  getDevPersonas,
  switchDevPersona,
  type DevPersona,
  type DevPersonasState,
} from '@/lib/api/devAuth';

// Slice 2 — floating DevAuth persona switcher. Mounts on every page; renders
// nothing when /dev-auth/personas returns 404 (production Entra auth). Shows
// current persona + lets the user flip to Owner / Guest / Admin to walk the
// guest-books-then-owner-confirms journey without restarting the API.
export const DevPersonaSwitcher = () => {
  const [state, setState] = useState<DevPersonasState | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setState(await getDevPersonas());
      } catch {
        // 404 = production auth; render nothing.
      }
    })();
  }, []);

  if (!state) return null;

  const onSwitch = async (p: DevPersona) => {
    if (p === state.current || busy) return;
    setBusy(true);
    try {
      await switchDevPersona(p);
      // Full reload — the cookie change means every subsequent request returns
      // different claims, and most pages already loaded with the old persona's
      // data.
      window.location.reload();
    } catch {
      setBusy(false);
    }
  };

  const currentInfo = state.options.find((o) => o.value === state.current) ?? state.options[0];

  return (
    <div className="fixed bottom-4 right-4 z-50">
      <div className="relative">
        <button
          onClick={() => setOpen((v) => !v)}
          className="flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-xs font-medium shadow-md hover:bg-accent"
        >
          <UserCircle className="h-4 w-4 text-muted-foreground" aria-hidden />
          <span>
            Dev persona: <span className="font-semibold">{currentInfo?.displayName ?? state.current}</span>
          </span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" aria-hidden />
        </button>
        {open && (
          <div className="absolute bottom-full right-0 mb-2 w-72 rounded-lg border border-border bg-background p-2 shadow-xl">
            <p className="px-2 pb-2 text-[10px] uppercase tracking-wider text-muted-foreground">
              Switch persona (DevAuth only)
            </p>
            {state.options.map((opt) => (
              <button
                key={opt.value}
                onClick={() => void onSwitch(opt.value)}
                disabled={busy}
                className={`flex w-full items-start gap-2 rounded-md px-2 py-2 text-left text-xs hover:bg-accent disabled:opacity-50 ${
                  opt.value === state.current ? 'bg-muted' : ''
                }`}
              >
                <div className="flex-1">
                  <div className="font-medium">{opt.displayName}</div>
                  <div className="text-muted-foreground">{opt.email}</div>
                  {opt.roles.length > 0 && (
                    <div className="mt-0.5 flex gap-1">
                      {opt.roles.map((r) => (
                        <span
                          key={r}
                          className="rounded bg-brand-orange-100 px-1.5 py-0.5 text-[10px] text-brand-maroon-700 dark:bg-brand-maroon-900/30 dark:text-brand-orange-200"
                        >
                          {r}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
                {opt.value === state.current && (
                  <Check className="mt-1 h-3.5 w-3.5 text-brand-maroon-600" aria-hidden />
                )}
              </button>
            ))}
            <p className="mt-2 border-t border-border px-2 pt-2 text-[10px] text-muted-foreground">
              Switching reloads the page so the new claims apply.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};
