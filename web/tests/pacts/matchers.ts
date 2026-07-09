import { MatchersV3 } from '@pact-foundation/pact';

/**
 * Slice OPS.1.2 — shared matcher helpers for the consumer.pact.ts. Keeps
 * per-flow interaction definitions declarative + minimizes the surface
 * that changes when API DTOs get reshaped.
 *
 * Matchers describe the SHAPE the SPA depends on; example values are just
 * placeholders. A production API response with different concrete values
 * (a different property title, a different guest count) still satisfies
 * the contract as long as the shape holds. That's the whole point of
 * consumer-driven contract testing — pin the shape, not the data.
 */
const {
  like,
  eachLike,
  regex,
  integer,
  decimal,
  boolean,
  string,
  uuid,
  datetime,
} = MatchersV3;

/** ISO 8601 datetime matcher. Use for `createdAt`, `checkIn` fields, etc. */
export const iso8601DateTime = (example: string = '2026-07-09T12:00:00Z') =>
  datetime("yyyy-MM-dd'T'HH:mm:ssX", example);

/**
 * Value shape of a `PropertySummaryDto` from GET /api/v1/properties.
 * Public list — no auth. Sits behind `PagedResult<T>` so the SPA reads
 * `items[].id`, `items[].title`, etc.
 */
export const propertySummaryMatcher = () =>
  like({
    id: uuid('11111111-1111-1111-1111-111111111111'),
    slug: regex(/^[a-z0-9\-]+$/, 'coastal-villa-p1'),
    title: string('Coastal Villa P1'),
    type: regex(/^(House|Apartment|Villa|Cottage|Cabin|Studio|Room)$/, 'Villa'),
    city: string('Colombo'),
    country: string('LK'),
    maxGuests: integer(6),
    bedrooms: integer(3),
    fromNightlyRate: decimal(150.0),
    currency: regex(/^[A-Z]{3}$/, 'USD'),
  });

/**
 * `PagedResult<T>` envelope matcher. `items` is `eachLike(one)` so the
 * contract accepts 1-or-more entries. `nextCursor` is nullable per the
 * `PagedResult` record. `total` is optional per the DTO.
 */
export const pagedPropertySummary = (min: number = 1) =>
  like({
    items: eachLike(propertySummaryMatcher(), min),
    nextCursor: string('null'),
    total: integer(5),
  });

// Re-exports so per-flow tests can pull matchers from one import.
export { like, eachLike, regex, integer, decimal, boolean, string, uuid, datetime };
