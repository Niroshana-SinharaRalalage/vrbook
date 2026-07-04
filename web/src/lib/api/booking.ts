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

// Slice 0.1 — checkout hold.
export interface Hold {
  readonly id: string;
  readonly propertyId: string;
  readonly checkin: string;
  readonly checkout: string;
  readonly expiresAt: string;
}

export const createHold = (
  propertyId: string,
  checkin: string,
  checkout: string,
  guests: number,
): Promise<Hold> =>
  apiFetch<Hold>('/api/v1/bookings/holds', {
    method: 'POST',
    body: { propertyId, checkin, checkout, guests },
  });

export const releaseHold = (holdId: string): Promise<void> =>
  apiFetch<void>(`/api/v1/bookings/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE' });

// Slice 2 — placeBooking now consumes a real hold (Slice 0 closed the race).
export interface PlaceBookingWithHoldBody extends PlaceBookingBody {
  readonly holdId: string;
}

export const placeBooking = (body: PlaceBookingWithHoldBody): Promise<Booking> =>
  apiFetch<Booking>('/api/v1/bookings', {
    method: 'POST',
    body: {
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

// ---- Slice 2 — Admin/Owner booking queue ---------------------------------
export interface AdminBookingSummary {
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
  readonly total: number;
  readonly currency: string;
  readonly tentativeUntil: string | null;
  readonly createdAt: string;
}

export const adminListBookings = (status?: BookingStatus): Promise<readonly AdminBookingSummary[]> =>
  apiFetch<readonly AdminBookingSummary[]>('/api/v1/admin/bookings', {
    query: status ? { status } : undefined,
  });

export const adminGetBooking = (id: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/admin/bookings/${encodeURIComponent(id)}`);

export const confirmBooking = (id: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/confirm`, { method: 'POST' });

export const rejectBooking = (id: string, reason: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/reject`, {
    method: 'POST',
    body: { reason },
  });

export const checkInBooking = (id: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/check-in`, { method: 'POST' });

export const checkOutBooking = (id: string): Promise<Booking> =>
  apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/check-out`, { method: 'POST' });

// ---- Payment ------------------------------------------------------------
export interface PaymentIntent {
  readonly id: string;
  readonly bookingId: string;
  readonly stripePaymentIntentId: string;
  readonly amount: Money;
  readonly status: string;
  readonly captureMethod: string;
  readonly createdAt: string;
}

export interface PaymentIntentForBooking {
  readonly paymentIntent: PaymentIntent;
  readonly clientSecret: string;
  readonly publishableKey: string;
}

export const getPaymentIntentForBooking = (bookingId: string): Promise<PaymentIntentForBooking> =>
  apiFetch<PaymentIntentForBooking>(`/api/v1/payments/intents/by-booking/${encodeURIComponent(bookingId)}`);

// ---- Slice 3 — property calendar + owner-created blocks -----------------

export type CalendarChannelKind = 'AirBnb' | 'VRBO' | 'BookingCom' | 'Direct';

export interface CalendarBookingEntry {
  readonly bookingId: string;
  readonly reference: string;
  readonly checkin: string;   // ISO date
  readonly checkout: string;
  readonly status: BookingStatus;
  readonly guestDisplayName: string;
}

export interface CalendarExternalEntry {
  readonly externalReservationId: string;
  readonly channel: CalendarChannelKind;
  readonly checkin: string;
  readonly checkout: string;
  readonly summary?: string | null;
}

export interface CalendarHoldEntry {
  readonly holdId: string;
  readonly checkin: string;
  readonly checkout: string;
  readonly expiresAt: string;
}

export interface CalendarBlockEntry {
  readonly blockId: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reason?: string | null;
}

export interface PropertyCalendar {
  readonly propertyId: string;
  readonly from: string;
  readonly to: string;
  readonly bookings: readonly CalendarBookingEntry[];
  readonly externalReservations: readonly CalendarExternalEntry[];
  readonly holds: readonly CalendarHoldEntry[];
  readonly blocks: readonly CalendarBlockEntry[];
}

export const getPropertyCalendar = (
  propertyId: string,
  from: string,
  to: string,
): Promise<PropertyCalendar> =>
  apiFetch<PropertyCalendar>(
    `/api/v1/properties/${encodeURIComponent(propertyId)}/calendar?from=${from}&to=${to}`,
  );

export interface AvailabilityBlock {
  readonly id: string;
  readonly propertyId: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly reason?: string | null;
  readonly createdAt: string;
}

export interface CreateAvailabilityBlockBody {
  readonly startDate: string;
  readonly endDate: string;
  readonly reason?: string | null;
}

export const listAvailabilityBlocks = (
  propertyId: string,
  from?: string,
  to?: string,
): Promise<readonly AvailabilityBlock[]> => {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  const qs = params.toString();
  return apiFetch<readonly AvailabilityBlock[]>(
    `/api/v1/properties/${encodeURIComponent(propertyId)}/blocks${qs ? `?${qs}` : ''}`,
  );
};

export const createAvailabilityBlock = (
  propertyId: string,
  body: CreateAvailabilityBlockBody,
): Promise<AvailabilityBlock> =>
  apiFetch<AvailabilityBlock>(
    `/api/v1/properties/${encodeURIComponent(propertyId)}/blocks`,
    { method: 'POST', body },
  );

export const deleteAvailabilityBlock = (
  propertyId: string,
  blockId: string,
): Promise<void> =>
  apiFetch<void>(
    `/api/v1/properties/${encodeURIComponent(propertyId)}/blocks/${encodeURIComponent(blockId)}`,
    { method: 'DELETE' },
  );
