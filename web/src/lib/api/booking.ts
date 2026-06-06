import { apiFetch } from './client';
import type { Money } from './pricing';

// ---- Wire shapes (mirror VrBook.Contracts.Dtos.Booking) -----------------
export type BookingStatus =
  | 'Draft'
  | 'Tentative'
  | 'Confirmed'
  | 'CheckedIn'
  | 'CheckedOut'
  | 'Completed'
  | 'Cancelled'
  | 'Rejected'
  | 'Disputed'
  | 'Refunded';

export interface BookingTotals {
  readonly subtotal: Money;
  readonly fees: Money;
  readonly taxes: Money;
  readonly discount: Money;
  readonly total: Money;
}

export interface BookingLineItem {
  readonly label: string;
  readonly kind: string;
  readonly quantity: number;
  readonly unitAmount: Money;
  readonly total: Money;
}

export interface BookingGuest {
  readonly fullName: string;
  readonly isPrimary: boolean;
}

export interface BookingSummary {
  readonly id: string;
  readonly reference: string;
  readonly propertyId: string;
  readonly propertyTitle: string;
  readonly checkinDate: string;
  readonly checkoutDate: string;
  readonly status: BookingStatus;
  readonly source: string;
  readonly total: Money;
  readonly createdAt: string;
}

export interface Booking {
  readonly id: string;
  readonly reference: string;
  readonly propertyId: string;
  readonly propertyTitle: string;
  readonly guestUserId: string;
  readonly guestDisplayName: string;
  readonly checkinDate: string;
  readonly checkoutDate: string;
  readonly guestCount: number;
  readonly status: BookingStatus;
  readonly source: string;
  readonly totals: BookingTotals;
  readonly lineItems: readonly BookingLineItem[];
  readonly cancellationPolicy: string;
  readonly paymentIntentId: string | null;
  readonly tentativeUntil: string | null;
  readonly confirmedAt: string | null;
  readonly cancelledAt: string | null;
  readonly cancellationReason: string | null;
  readonly guests: readonly BookingGuest[];
  readonly specialRequests: string | null;
  readonly createdAt: string;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly nextCursor: string | null;
  readonly total: number | null;
}

// ---- Requests + API calls ------------------------------------------------
export interface PlaceBookingBody {
  readonly propertyId: string;
  readonly checkinDate: string;
  readonly checkoutDate: string;
  readonly guestCount: number;
  readonly guests: readonly BookingGuest[];
  readonly specialRequests?: string | null;
  readonly agreedToHouseRules: boolean;
  readonly applyLoyaltyDiscount?: boolean;
}

export const placeBooking = (body: PlaceBookingBody): Promise<Booking> =>
  apiFetch<Booking>('/api/v1/bookings', {
    method: 'POST',
    body: {
      // The hold flow is deferred (A4.1) - send a zero Guid for the field.
      holdId: '00000000-0000-0000-0000-000000000000',
      applyLoyaltyDiscount: false,
      ...body,
    },
  });

export const getBooking = (id: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}`);

export const myBookings = (cursor?: string): Promise<PagedResult<BookingSummary>> =>
  apiFetch<PagedResult<BookingSummary>>('/api/v1/bookings', {
    query: { cursor, limit: 20 },
  });

export const cancelBooking = (id: string, reason: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/cancel`, {
    method: 'POST',
    body: { reason },
  });
