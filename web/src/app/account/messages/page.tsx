'use client';

import { Suspense, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useQueryClient } from '@tanstack/react-query';
import { getCurrentUser, type CurrentUser } from '@/lib/api/me';
import { listThreads, type Thread } from '@/lib/api/messaging';
import { ApiProblemError } from '@/lib/api/client';
import { useThreadPoller } from '@/hooks/useThreadPoller';
import { useAuthedQuery } from '@/hooks/useAuthedQuery';
import { SignInGate } from '@/components/auth/SignInGate';
import ThreadInbox from '@/components/messaging/ThreadInbox';
import ConversationPane from '@/components/messaging/ConversationPane';

const extractErr = (e: unknown, fallback: string): string => {
  if (e instanceof ApiProblemError) return e.problem.detail ?? e.message;
  if (e instanceof Error) return e.message;
  return fallback;
};

// Slice OPS.M.10.2 F11.7.4.5 — both /me + /threads moved onto
// useAuthedQuery (gated on MSAL readiness). Pre-fix the page fired
// both calls in useEffects on mount and 401'd on cold MSAL state.
// The 30-second poller (useThreadPoller) now invalidates the threads
// query instead of re-running the manual fetcher.
const ME_QK = ['me'] as const;
const THREADS_QK = ['threads'] as const;

const AccountMessagesBody = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const threadFromQuery = searchParams.get('thread');
  const qc = useQueryClient();

  const meQ = useAuthedQuery<CurrentUser>({
    queryKey: [...ME_QK],
    queryFn: getCurrentUser,
  });
  const threadsQ = useAuthedQuery<readonly Thread[]>({
    queryKey: [...THREADS_QK],
    queryFn: () => listThreads(),
  });

  const threads = threadsQ.data ?? [];
  const [activeThreadId, setActiveThreadId] = useState<string | null>(threadFromQuery);

  // Default the active thread to the first one in the list as soon as
  // threads arrive (one-shot — only when no thread is already active).
  if (!activeThreadId && threads.length > 0) {
    const first = threads[0];
    if (first) setActiveThreadId(first.id);
  }

  useThreadPoller(() => qc.invalidateQueries({ queryKey: [...THREADS_QK] }));

  if (meQ.needsSignIn || threadsQ.needsSignIn) {
    return <SignInGate title="Sign in to view your messages" />;
  }

  const error = meQ.isError
    ? extractErr(meQ.error, 'Failed to load your account.')
    : threadsQ.isError
      ? extractErr(threadsQ.error, 'Failed to load conversations.')
      : null;

  const onSelect = (threadId: string) => {
    setActiveThreadId(threadId);
    router.replace(`/account/messages?thread=${encodeURIComponent(threadId)}`);
  };

  return (
    <div className="space-y-4">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Messages</h1>
        <p className="text-sm text-muted-foreground">
          Conversations with your hosts. Updates every 30 seconds while this tab is open.
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
            counterpartySide="guest"
            onSelect={onSelect}
            loading={threadsQ.isLoading}
          />
        </aside>
        <section className="col-span-12 md:col-span-8">
          <ConversationPane
            threadId={activeThreadId}
            currentUserId={meQ.data?.id ?? null}
            onMessageSent={() => qc.invalidateQueries({ queryKey: [...THREADS_QK] })}
          />
        </section>
      </div>
    </div>
  );
};

const AccountMessagesPage = () => (
  <Suspense fallback={<p className="text-sm text-muted-foreground">Loading…</p>}>
    <AccountMessagesBody />
  </Suspense>
);

export default AccountMessagesPage;
