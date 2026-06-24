'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { getCurrentUser } from '@/lib/api/me';
import { listThreads, type Thread } from '@/lib/api/messaging';
import { ApiProblemError } from '@/lib/api/client';
import { useThreadPoller } from '@/hooks/useThreadPoller';
import ThreadInbox from '@/components/messaging/ThreadInbox';
import ConversationPane from '@/components/messaging/ConversationPane';

const extractErr = (e: unknown, fallback: string): string => {
  if (e instanceof ApiProblemError) return e.problem.detail ?? e.message;
  if (e instanceof Error) return e.message;
  return fallback;
};

const AdminMessagesPage = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const threadFromQuery = searchParams.get('thread');

  const [threads, setThreads] = useState<readonly Thread[]>([]);
  const [activeThreadId, setActiveThreadId] = useState<string | null>(threadFromQuery);
  const [currentUserId, setCurrentUserId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        const me = await getCurrentUser();
        setCurrentUserId(me.id);
      } catch (e) {
        setError(extractErr(e, 'Failed to load your account.'));
      }
    })();
  }, []);

  const reloadThreads = async (): Promise<void> => {
    try {
      const list = await listThreads();
      setThreads(list);
      if (!activeThreadId && list.length > 0) {
        const first = list[0];
        if (first) setActiveThreadId(first.id);
      }
      setLoading(false);
    } catch (e) {
      setError(extractErr(e, 'Failed to load conversations.'));
      setLoading(false);
    }
  };

  useThreadPoller(reloadThreads);

  const onSelect = (threadId: string) => {
    setActiveThreadId(threadId);
    // Push the selection into the URL so deep links keep working.
    router.replace(`/admin/messages?thread=${encodeURIComponent(threadId)}`);
  };

  return (
    <div className="space-y-4">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Messages</h1>
        <p className="text-sm text-muted-foreground">
          Conversations from every booking you host. Updates every 30 seconds while this tab is open.
        </p>
      </header>

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {error}
        </div>
      )}

      <div className="grid h-[600px] grid-cols-12 overflow-hidden rounded-xl border border-border bg-card">
        <aside className="col-span-12 overflow-y-auto border-r border-border md:col-span-4">
          <ThreadInbox
            threads={threads}
            activeThreadId={activeThreadId}
            counterpartySide="owner"
            onSelect={onSelect}
            loading={loading}
          />
        </aside>
        <section className="col-span-12 md:col-span-8">
          <ConversationPane
            threadId={activeThreadId}
            currentUserId={currentUserId}
            onMessageSent={() => void reloadThreads()}
          />
        </section>
      </div>
    </div>
  );
};

export default AdminMessagesPage;
