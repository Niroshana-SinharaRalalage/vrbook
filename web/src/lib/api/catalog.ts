import { apiFetch } from './client';

// ---- Wire shapes (mirror VrBook.Contracts.Dtos.Catalog) -----------------
export interface PropertySummary {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly type: string;
  readonly city: string;
  readonly country: string;
  readonly maxGuests: number;
  readonly bedrooms: number;
  readonly fromNightlyRate: number | null;
  readonly currency: string;
  readonly averageRating: number | null;
  readonly ratingCount: number;
  readonly primaryImageUrl: string | null;
}

export interface PropertyImage {
  readonly id: string;
  readonly url: string;
  readonly caption: string | null;
  readonly sortOrder: number;
  readonly isPrimary: boolean;
}

export interface Amenity {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly icon: string | null;
  readonly category: string;
  readonly isActive: boolean;
}

export interface PropertyAddress {
  readonly street: string;
  readonly city: string;
  readonly state: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly latitude: number;
  readonly longitude: number;
}

export interface PropertyDetail {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly description: string;
  readonly type: string;
  readonly address: PropertyAddress;
  readonly maxGuests: number;
  readonly bedrooms: number;
  readonly bathrooms: number;
  readonly beds: number;
  readonly checkinFrom: string;
  readonly checkinTo: string;
  readonly checkoutBy: string;
  readonly isActive: boolean;
  readonly reviewsEnabled: boolean;
  readonly dynamicPricingEnabled: boolean;
  readonly messagingEnabled: boolean;
  readonly averageRating: number | null;
  readonly ratingCount: number;
  readonly images: readonly PropertyImage[];
  readonly amenities: readonly Amenity[];
  readonly houseRules: readonly string[];
  // Slice OPS.M.16 — property-default turnover window (hours). Falls back
  // to the domain default of 24 when the wire payload omits it (older API
  // versions or an unpopulated field).
  readonly turnoverHours?: number;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly nextCursor: string | null;
  readonly total: number | null;
}

// ---- Search params (mirror SearchPropertiesRequest) ---------------------
export interface SearchPropertiesQuery {
  destination?: string;
  checkin?: string;
  checkout?: string;
  guests?: number;
  minPrice?: number;
  maxPrice?: number;
  amenityCodes?: readonly string[];
  minRating?: number;
  sort?: string;
  cursor?: string;
  limit?: number;
}

// ---- API calls ----------------------------------------------------------
export const searchProperties = (q: SearchPropertiesQuery = {}): Promise<PagedResult<PropertySummary>> => {
  const query: Record<string, string | number | undefined> = {
    destination: q.destination,
    checkin: q.checkin,
    checkout: q.checkout,
    guests: q.guests,
    minPrice: q.minPrice,
    maxPrice: q.maxPrice,
    minRating: q.minRating,
    sort: q.sort,
    cursor: q.cursor,
    limit: q.limit,
  };
  // Repeated amenityCodes - apiFetch only accepts scalar; serialise manually.
  let path = '/api/v1/properties';
  if (q.amenityCodes && q.amenityCodes.length > 0) {
    const search = new URLSearchParams();
    q.amenityCodes.forEach((c) => search.append('amenityCodes', c));
    path += `?${search.toString()}`;
  }
  return apiFetch<PagedResult<PropertySummary>>(path, { query, anonymous: true });
};

export const getPropertyBySlug = (slug: string): Promise<PropertyDetail> =>
  apiFetch<PropertyDetail>(`/api/v1/properties/${encodeURIComponent(slug)}`, { anonymous: true });

export const listAmenities = (): Promise<readonly Amenity[]> =>
  apiFetch<readonly Amenity[]>('/api/v1/amenities', { anonymous: true });

export interface BlockedRange {
  readonly start: string;   // YYYY-MM-DD
  readonly end: string;     // YYYY-MM-DD (exclusive)
}

export interface Availability {
  readonly propertyId: string;
  readonly from: string;
  readonly to: string;
  readonly blocked: readonly BlockedRange[];
}

export const getAvailability = (propertyId: string, from: string, to: string): Promise<Availability> =>
  apiFetch<Availability>(`/api/v1/properties/${encodeURIComponent(propertyId)}/availability`, {
    query: { from, to },
    anonymous: true,
  });

// ---- Admin amenity CRUD (A2.2) -----------------------------------------
export interface CreateAmenityBody {
  readonly code: string;
  readonly name: string;
  readonly icon: string | null;
  readonly category: string;
}

export interface UpdateAmenityBody {
  readonly name: string;
  readonly icon: string | null;
  readonly category: string;
}

export const adminListAmenities = (): Promise<readonly Amenity[]> =>
  apiFetch<readonly Amenity[]>('/api/v1/admin/amenities');

export const adminCreateAmenity = (body: CreateAmenityBody): Promise<Amenity> =>
  apiFetch<Amenity>('/api/v1/admin/amenities', {
    method: 'POST',
    body,
  });

export const adminUpdateAmenity = (id: string, body: UpdateAmenityBody): Promise<Amenity> =>
  apiFetch<Amenity>(`/api/v1/admin/amenities/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body,
  });

export const adminDisableAmenity = (id: string): Promise<Amenity> =>
  apiFetch<Amenity>(`/api/v1/admin/amenities/${encodeURIComponent(id)}/disable`, {
    method: 'POST',
  });

export const adminEnableAmenity = (id: string): Promise<Amenity> =>
  apiFetch<Amenity>(`/api/v1/admin/amenities/${encodeURIComponent(id)}/enable`, {
    method: 'POST',
  });

export const adminDeleteAmenity = (id: string): Promise<void> =>
  apiFetch<void>(`/api/v1/admin/amenities/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });

// ---- Admin property management (Slice 1) -------------------------------
export interface AdminPropertySummary {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly type: string;
  readonly city: string;
  readonly country: string;
  readonly maxGuests: number;
  readonly bedrooms: number;
  readonly isActive: boolean;
  readonly ownerUserId: string;
  readonly createdAt: string;
  readonly primaryImageUrl: string | null;
}

export interface PropertyAddressBody {
  readonly street: string;
  readonly city: string;
  readonly state: string;
  readonly postalCode: string;
  readonly countryCode: string;
  readonly latitude: number;
  readonly longitude: number;
}

export interface CreatePropertyBody {
  readonly title: string;
  readonly description: string;
  readonly type: string; // 'House' | 'Apartment' | 'Cabin' | 'Cottage' | 'Studio' | 'Villa'
  readonly address: PropertyAddressBody;
  readonly maxGuests: number;
  readonly bedrooms: number;
  readonly bathrooms: number;
  readonly beds: number;
  readonly checkinFrom: string; // HH:mm
  readonly checkinTo: string;
  readonly checkoutBy: string;
  readonly houseRules: readonly string[];
  readonly amenityIds: readonly string[];
  readonly turnoverHours?: number; // Slice OPS.M.16 — [0, 168]; defaults to 24 server-side.
}

export interface UpdatePropertyBody extends CreatePropertyBody {
  readonly reviewsEnabled: boolean;
  readonly dynamicPricingEnabled: boolean;
  readonly messagingEnabled: boolean;
  readonly isActive: boolean;
}

export const adminListMyProperties = (): Promise<readonly AdminPropertySummary[]> =>
  apiFetch<readonly AdminPropertySummary[]>('/api/v1/admin/properties');

export const adminGetPropertyById = (id: string): Promise<PropertyDetail> =>
  apiFetch<PropertyDetail>(`/api/v1/admin/properties/${encodeURIComponent(id)}`);

export const createProperty = (body: CreatePropertyBody): Promise<PropertyDetail> =>
  apiFetch<PropertyDetail>('/api/v1/properties', {
    method: 'POST',
    body,
  });

export const updateProperty = (id: string, body: UpdatePropertyBody): Promise<PropertyDetail> =>
  apiFetch<PropertyDetail>(`/api/v1/properties/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body,
  });
