namespace VrBook.Contracts.Events;

// OPS.M.4 Step 1 — PropertyCreated + PropertyImageAdded bumped; PropertyUpdated,
// PropertyDeactivated, PropertyRatingRecomputeRequested are same-module-only
// consumers and derive tenant from the FK.

public sealed record PropertyCreated(
    Guid PropertyId,
    Guid OwnerUserId,
    string Slug,
    string Title,
    Guid TenantId) : DomainEvent;

public sealed record PropertyUpdated(Guid PropertyId) : DomainEvent;

public sealed record PropertyImageAdded(
    Guid PropertyId,
    Guid ImageId,
    string BlobPath,
    Guid TenantId) : DomainEvent;

// VRB-101 — same-module consumer (blob cleanup is handled inline in the delete
// handler after SaveChanges); event exists for audit/outbox symmetry.
public sealed record PropertyImageRemoved(
    Guid PropertyId,
    Guid ImageId,
    Guid TenantId) : DomainEvent;

public sealed record PropertyDeactivated(Guid PropertyId, string Reason) : DomainEvent;

/// <summary>Published by Reviews module — Catalog updates <c>rating_avg</c> + <c>rating_count</c>.</summary>
public sealed record PropertyRatingRecomputeRequested(Guid PropertyId) : DomainEvent;
