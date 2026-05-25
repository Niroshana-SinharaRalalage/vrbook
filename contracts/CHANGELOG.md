# Contracts CHANGELOG

> CI enforces that any change to `openapi.yaml` or `src/VrBook.Contracts/**` ships with a
> bumped entry below and a PR labelled `contracts-change`. See proposal §20.3.

Versioning: **independent semver** for the contract surface, distinct from the application version.

- `MAJOR` — any breaking change to an existing endpoint, DTO, event, or interface.
- `MINOR` — additive change (new endpoint, new optional field, new event type, new interface).
- `PATCH` — documentation only, or non-semantic refactor.

---

## [0.2.0] — 2026-05-25

### Changed (BREAKING for unreleased clients only)
- `IBookingAvailabilityReader.GetDailyAvailabilityAsync`: parameter `from`/`to` renamed to `fromDate`/`toDate` (CA1716 fix — `to` collides with VB.NET keyword).
- `IDomainEventPublisher.PublishAsync`: parameter `@event` → `domainEvent`, `events` → `domainEvents` (same reason).

### Added
- `/me`, `PUT /me`, `DELETE /me`, `GET /admin/users` are now real endpoints — wired to the Identity module's MediatR handlers. Same OpenAPI shape as v0.1 (which was stubbed at 501); contract surface unchanged.
- `DevAuth:AllowAnonymous` configuration toggle — when true, every request authenticates as a synthetic "Dev Owner" so the API is reachable in local dev without a real B2C tenant. Disabled by default; meant for `Development` only.

### Removed
- Nothing removed — only behavior added behind existing endpoint shapes.

## [0.1.0] — 2026-01-15

### Added
- Initial contract surface scaffolded by Agent A0.
- OpenAPI v0.1 with every endpoint from proposal §6.2 (all return 501 in the implementation).
- `VrBook.Contracts.Common`: `Money`, `DateRange`, `Address`, `PagedResult<T>`, `OffsetPagedResult<T>`, `ProblemTypes`.
- `VrBook.Contracts.Enums`: `BookingStatus`, `BookingSource`, `PaymentStatus`, `RefundStatus`, `PropertyType`, `ChannelKind`, `LoyaltyTier`, `CancellationPolicyCode`, `ReviewStatus`, `SyncRunStatus`, `SyncConflictResolution`, `NotificationChannel`, `NotificationStatus`, `PricingRuleKind`, `PricingAdjustmentKind`, `FeeKind`, `FeeBasis`.
- `VrBook.Contracts.Dtos`: per-context DTO records covering every endpoint in §6.2.
- `VrBook.Contracts.Events`: `IDomainEvent` + `DomainEvent` base + per-context event records covering every event in §4.3.
- `VrBook.Contracts.Interfaces`:
  - `ICurrentUser`, `IDateTimeProvider`, `IUnitOfWork`, `IIdempotencyStore` — host concerns.
  - `IDistributedLock`, `IHoldStore` — booking hold (proposal §7.3).
  - `IBookingAvailabilityReader` — Catalog → Booking bridge.
  - `IPaymentService` — Booking → Payment bridge.
  - `ITaxCalculator` — Pricing → Tax bridge (Stripe Tax in A5).
  - `ILoyaltyDiscountResolver` — Pricing → Loyalty bridge.
  - `IExternalChannel`, `IExternalChannelConflictChecker` — Sync extensibility (proposal §8.4).
  - `INotificationSender` — Notifications boundary.
  - `IFeatureToggle` — toggle resolver.
  - `IBlobStorage` — Catalog + Messaging asset adapter.
  - `IDomainEventPublisher` — outbox.

### Known stub responses
Every endpoint returns `501 Not Implemented` with a Problem response that names the agent
that will replace it. The OpenAPI `responses` blocks declare the eventual success shapes
even though the implementation is not yet there — this lets the frontend generate the API
client from day one without false coupling to stub responses.
