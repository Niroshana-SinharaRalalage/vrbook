import { apiFetch } from './client';

// ---- Wire shapes (mirror VrBook.Contracts.Dtos.Sync) -------------------
export type ChannelKind = 'AirBnb' | 'Vrbo' | 'BookingCom' | 'Other';

export type SyncConflictResolution =
  | 'Pending'
  | 'OwnerKeptDirect'
  | 'OwnerCancelledDirect'
  | 'AutoCancelled'
  | 'ManualOverride';

export interface ChannelFeed {
  readonly id: string;
  readonly propertyId: string;
  readonly propertyTitle: string;
  readonly channel: ChannelKind;
  readonly inboundUrl: string;
  readonly outboundFeedUrl: string;
  readonly pollIntervalMinutes: number;
  readonly isEnabled: boolean;
  readonly lastSuccessAt: string | null;
  readonly lastAttemptAt: string | null;
  readonly lastError: string | null;
}

export interface CreateChannelFeedBody {
  readonly propertyId: string;
  readonly channel: ChannelKind | number; // API enum is integer over the wire
  readonly inboundUrl: string;
  readonly pollIntervalMinutes: number;
}

export interface UpdateChannelFeedBody {
  readonly inboundUrl: string;
  readonly pollIntervalMinutes: number;
  readonly isEnabled: boolean;
}

export interface SyncConflict {
  readonly id: string;
  readonly propertyId: string;
  readonly propertyTitle: string;
  readonly externalReservationId: string;
  readonly externalSummary: string;
  readonly externalCheckin: string;
  readonly externalCheckout: string;
  readonly bookingId: string;
  readonly bookingReference: string;
  readonly bookingCheckin: string;
  readonly bookingCheckout: string;
  readonly resolution: SyncConflictResolution;
  readonly resolutionNotes: string | null;
  readonly detectedAt: string;
  readonly resolvedAt: string | null;
}

export interface ResolveConflictBody {
  readonly resolution: SyncConflictResolution | number;
  readonly notes: string;
}

// ---- API ---------------------------------------------------------------
export const listChannelFeeds = (): Promise<readonly ChannelFeed[]> =>
  apiFetch<readonly ChannelFeed[]>('/api/v1/admin/channel-feeds');

export const createChannelFeed = (body: CreateChannelFeedBody): Promise<ChannelFeed> =>
  apiFetch<ChannelFeed>('/api/v1/admin/channel-feeds', { method: 'POST', body });

export const updateChannelFeed = (id: string, body: UpdateChannelFeedBody): Promise<ChannelFeed> =>
  apiFetch<ChannelFeed>(`/api/v1/admin/channel-feeds/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body,
  });

export const deleteChannelFeed = (id: string): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/channel-feeds/${encodeURIComponent(id)}`, { method: 'DELETE' });

export const listSyncConflicts = (): Promise<readonly SyncConflict[]> =>
  apiFetch<readonly SyncConflict[]>('/api/v1/admin/sync-conflicts');

export const resolveSyncConflict = (id: string, body: ResolveConflictBody): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/sync-conflicts/${encodeURIComponent(id)}/resolve`, {
    method: 'POST',
    body,
  });
