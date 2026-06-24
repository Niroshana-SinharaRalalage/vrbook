import { apiFetch } from './client';

// Mirror VrBook.Contracts.Dtos.ThreadDto / MessageDto / MessageAttachmentDto.

export interface MessageAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly downloadUrl: string;
}

export interface Thread {
  readonly id: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly guestUserId: string;
  readonly guestDisplayName: string;
  readonly ownerUserId: string;
  readonly ownerDisplayName: string;
  readonly unreadCount: number;
  readonly lastMessageAt: string | null;
  readonly lastMessagePreview: string | null;
}

export interface Message {
  readonly id: string;
  readonly threadId: string;
  readonly senderUserId: string;
  readonly senderDisplayName: string;
  readonly body: string;
  readonly attachments: readonly MessageAttachment[];
  readonly createdAt: string;
  readonly readAt: string | null;
}

export const listThreads = (bookingId?: string): Promise<readonly Thread[]> =>
  apiFetch<readonly Thread[]>(
    `/api/v1/threads${bookingId ? `?bookingId=${encodeURIComponent(bookingId)}` : ''}`,
  );

export const getThread = (threadId: string): Promise<Thread> =>
  apiFetch<Thread>(`/api/v1/threads/${encodeURIComponent(threadId)}`);

export const listMessages = (threadId: string): Promise<readonly Message[]> =>
  apiFetch<readonly Message[]>(`/api/v1/threads/${encodeURIComponent(threadId)}/messages`);

export const sendMessage = (threadId: string, body: string): Promise<Message> =>
  apiFetch<Message>(`/api/v1/threads/${encodeURIComponent(threadId)}/messages`, {
    method: 'POST',
    body: { body, attachmentIds: null },
  });

export const markThreadRead = (threadId: string, upToMessageId: string): Promise<void> =>
  apiFetch<void>(`/api/v1/threads/${encodeURIComponent(threadId)}/read`, {
    method: 'POST',
    body: { upToMessageId },
  });
