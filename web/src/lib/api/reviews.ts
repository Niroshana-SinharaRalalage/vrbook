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
