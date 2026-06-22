import { apiFetch } from './client';
import type { PagedResult } from './booking';

export interface ReviewResponse {
  readonly id: string;
  readonly body: string;
  readonly createdAt: string;
}

export interface Review {
  readonly id: string;
  readonly bookingId: string;
  readonly propertyId: string;
  readonly guestUserId: string;
  readonly guestDisplayName: string;
  readonly rating: number;
  readonly body: string;
  readonly status: 'Pending' | 'Approved' | 'Rejected' | 'Hidden';
  readonly publishedAt: string | null;
  readonly response: ReviewResponse | null;
  readonly createdAt: string;
}

export interface SubmitReviewBody {
  readonly rating: number;
  readonly body: string;
}

export const submitReview = (bookingId: string, body: SubmitReviewBody): Promise<Review> =>
  apiFetch<Review>(`/api/v1/bookings/${encodeURIComponent(bookingId)}/review`, {
    method: 'POST',
    body,
  });

export const listReviewsForProperty = (propertyId: string, cursor?: string): Promise<PagedResult<Review>> =>
  apiFetch<PagedResult<Review>>(`/api/v1/properties/${encodeURIComponent(propertyId)}/reviews`, {
    query: { cursor, limit: 20 },
    anonymous: true,
  });

export const respondToReview = (id: string, body: string): Promise<void> =>
  apiFetch<void>(`/api/v1/reviews/${encodeURIComponent(id)}/response`, {
    method: 'POST',
    body: { body },
  });

export const adminListReviews = (status?: Review['status']): Promise<readonly Review[]> => {
  const params = new URLSearchParams();
  if (status) params.set('status', status);
  const qs = params.toString();
  return apiFetch<readonly Review[]>(`/api/v1/admin/reviews${qs ? `?${qs}` : ''}`);
};

export const adminHideReview = (id: string): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/reviews/${encodeURIComponent(id)}/hide`, { method: 'POST' });

export const adminRestoreReview = (id: string): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/reviews/${encodeURIComponent(id)}/restore`, { method: 'POST' });

export const adminRejectReview = (id: string): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/reviews/${encodeURIComponent(id)}/reject`, { method: 'POST' });
