'use client';

import { useEffect, useRef, useState } from 'react';
import { Send } from 'lucide-react';
import {
  listMessages,
  markThreadRead,
  sendMessage,
  type Message,
} from '@/lib/api/messaging';
import { ApiProblemError } from '@/lib/api/client';
import { useThreadPoller } from '@/hooks/useThreadPoller';

interface ConversationPaneProps {
  readonly threadId: string | null;
  readonly currentUserId: string | null;
  readonly onMessageSent?: () => void;
}

const extractErr = (e: unknown, fallback: string): string => {
  if (e instanceof ApiProblemError) return e.problem.detail ?? e.message;
  if (e instanceof Error) return e.message;
  return fallback;
};

const ConversationPane = ({ threadId, currentUserId, onMessageSent }: ConversationPaneProps) => {
  const [messages, setMessages] = useState<readonly Message[]>([]);
  const [body, setBody] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const lastMarkedReadIdRef = useRef<string | null>(null);

  const reload = async (): Promise<void> => {
    if (!threadId) return;
    try {
      const msgs = await listMessages(threadId);
      setMessages(msgs);
      // Mark the latest message as read so the unread badge clears. Only do
      // this when the latest message is not authored by me — otherwise marking
      // my own message as read is a no-op on the backend.
      const latest = msgs[msgs.length - 1];
      if (
        latest &&
        latest.senderUserId !== currentUserId &&
        latest.id !== lastMarkedReadIdRef.current
      ) {
        try {
          await markThreadRead(threadId, latest.id);
          lastMarkedReadIdRef.current = latest.id;
        } catch {
          /* swallow — the worst case is the unread badge sticks around for the next poll */
        }
      }
    } catch (e) {
      setError(extractErr(e, 'Failed to load messages.'));
    }
  };

  // Reset state when the active thread changes.
  useEffect(() => {
    setMessages([]);
    setError(null);
    lastMarkedReadIdRef.current = null;
  }, [threadId]);

  useThreadPoller(reload);

  // Auto-scroll to the bottom on new messages.
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  const onSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!threadId) return;
    const text = body.trim();
    if (!text) return;
    setSending(true);
    setError(null);
    try {
      const created = await sendMessage(threadId, text);
      setMessages((prev) => [...prev, created]);
      setBody('');
      onMessageSent?.();
    } catch (e2) {
      setError(extractErr(e2, 'Failed to send.'));
    } finally {
      setSending(false);
    }
  };

  if (!threadId) {
    return (
      <div className="flex h-full items-center justify-center p-8 text-sm text-muted-foreground">
        Select a conversation to read or reply.
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <div ref={scrollRef} className="flex-1 space-y-2 overflow-y-auto p-4">
        {error && (
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">
            {error}
          </div>
        )}
        {messages.length === 0 && !error && (
          <p className="text-center text-xs text-muted-foreground">No messages yet — say hi.</p>
        )}
        {messages.map((m) => {
          const mine = m.senderUserId === currentUserId;
          return (
            <div key={m.id} className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
              <div
                className={`max-w-[75%] rounded-lg px-3 py-2 text-sm ${
                  mine
                    ? 'bg-brand-maroon-700 text-white'
                    : 'bg-muted text-foreground'
                }`}
              >
                {!mine && <p className="mb-0.5 text-[11px] font-medium opacity-70">{m.senderDisplayName}</p>}
                <p className="whitespace-pre-wrap">{m.body}</p>
                <p
                  className={`mt-1 text-[10px] ${
                    mine ? 'text-white/70' : 'text-muted-foreground'
                  }`}
                >
                  {new Date(m.createdAt).toLocaleString()}
                  {mine && m.readAt && ' · Read'}
                </p>
              </div>
            </div>
          );
        })}
      </div>

      <form onSubmit={onSend} className="border-t border-border p-3">
        <div className="flex items-end gap-2">
          <textarea
            value={body}
            onChange={(e) => setBody(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                void onSend(e as unknown as React.FormEvent);
              }
            }}
            rows={2}
            maxLength={4000}
            disabled={sending}
            placeholder="Type a message — Enter to send, Shift+Enter for newline"
            className="flex-1 resize-none rounded-md border border-border bg-background p-2 text-sm"
          />
          <button
            type="submit"
            disabled={sending || !body.trim()}
            className="inline-flex items-center gap-1 rounded-md bg-brand-maroon-700 px-3 py-2 text-sm text-white hover:bg-brand-maroon-800 disabled:opacity-50"
          >
            <Send className="h-3.5 w-3.5" />
            Send
          </button>
        </div>
      </form>
    </div>
  );
};

export default ConversationPane;
