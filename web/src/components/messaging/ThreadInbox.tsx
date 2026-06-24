'use client';

import { type Thread } from '@/lib/api/messaging';

const formatRelative = (iso: string | null): string => {
  if (!iso) return '';
  const then = new Date(iso).getTime();
  const now = Date.now();
  const min = Math.round((now - then) / 60_000);
  if (min < 1) return 'just now';
  if (min < 60) return `${min}m ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const d = Math.round(hr / 24);
  return `${d}d ago`;
};

interface ThreadInboxProps {
  readonly threads: readonly Thread[];
  readonly activeThreadId: string | null;
  readonly counterpartySide: 'guest' | 'owner';
  readonly onSelect: (threadId: string) => void;
  readonly loading: boolean;
}

const ThreadInbox = ({
  threads,
  activeThreadId,
  counterpartySide,
  onSelect,
  loading,
}: ThreadInboxProps) => {
  if (loading && threads.length === 0) {
    return <p className="p-4 text-sm text-muted-foreground">Loading…</p>;
  }
  if (threads.length === 0) {
    return (
      <p className="p-4 text-sm text-muted-foreground">
        No conversations yet. Threads are created automatically when a booking is confirmed.
      </p>
    );
  }
  return (
    <ul className="divide-y divide-border">
      {threads.map((t) => {
        const isActive = t.id === activeThreadId;
        const counterparty = counterpartySide === 'owner' ? t.guestDisplayName : t.ownerDisplayName;
        return (
          <li key={t.id}>
            <button
              type="button"
              onClick={() => onSelect(t.id)}
              className={`w-full px-3 py-2 text-left text-sm transition-colors ${
                isActive
                  ? 'bg-brand-maroon-50 dark:bg-brand-maroon-950/40'
                  : 'hover:bg-accent'
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="truncate font-medium">{counterparty}</span>
                {t.unreadCount > 0 && (
                  <span className="rounded-full bg-brand-maroon-700 px-1.5 py-0.5 text-[10px] font-medium text-white">
                    {t.unreadCount}
                  </span>
                )}
              </div>
              <div className="mt-0.5 flex items-center justify-between gap-2 text-[11px] text-muted-foreground">
                <span className="font-mono">{t.bookingReference}</span>
                <span>{formatRelative(t.lastMessageAt)}</span>
              </div>
              {t.lastMessagePreview && (
                <p className="mt-1 truncate text-xs text-muted-foreground">
                  {t.lastMessagePreview}
                </p>
              )}
            </button>
          </li>
        );
      })}
    </ul>
  );
};

export default ThreadInbox;
