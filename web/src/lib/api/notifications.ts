import { apiFetch } from './client';

// Mirror NotificationKind / NotificationStatus from
// src/Modules/VrBook.Modules.Notifications/Domain/NotificationLog.cs
export type NotificationStatus =
  | 'Queued'
  | 'Sent'
  | 'Failed'
  | 'DeadLetter'
  | 'Sending';

export type NotificationKind =
  | 'BookingPlaced'
  | 'BookingConfirmed'
  | 'BookingRejected'
  | 'BookingCancelled'
  | 'BookingCheckedIn'
  | 'BookingCheckedOut'
  | 'BookingCompleted'
  | 'MessageDeliveryDeferred'
  | 'PaymentCaptured'
  | 'RefundIssued'
  | 'ReviewSubmitted'
  | 'OwnerTentativeReceived'
  | 'OwnerActionRequiredReminder'
  | 'OwnerAutoConfirmed'
  | 'OwnerCancellationAlert'
  | 'OwnerSyncConflict';

export interface NotificationLog {
  readonly id: string;
  readonly kind: NotificationKind;
  readonly status: NotificationStatus;
  readonly recipientUserId: string;
  readonly recipientEmail: string;
  readonly subject: string;
  readonly retryCount: number;
  readonly lastError?: string | null;
  readonly sentAt?: string | null;
  readonly createdAt: string;
}

export const adminListNotifications = (
  status?: NotificationStatus,
  limit = 100,
): Promise<readonly NotificationLog[]> => {
  const params = new URLSearchParams();
  if (status) params.set('status', status);
  if (limit) params.set('limit', String(limit));
  return apiFetch<readonly NotificationLog[]>(
    `/api/v1/admin/notifications?${params.toString()}`,
  );
};

export const adminRetryNotification = (id: string): Promise<void> =>
  apiFetch<void>(
    `/api/v1/admin/notifications/${encodeURIComponent(id)}/retry`,
    { method: 'POST' },
  );
