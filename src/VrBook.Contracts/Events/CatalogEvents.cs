namespace VrBook.Contracts.Events;

public sealed record PropertyCreated(Guid PropertyId, Guid OwnerUserId, string Slug, string Title) : DomainEvent;

public sealed record PropertyUpdated(Guid PropertyId) : DomainEvent;

public sealed record PropertyImageAdded(Guid PropertyId, Guid ImageId, string BlobPath) : DomainEvent;

public sealed record PropertyDeactivated(Guid PropertyId, string Reason) : DomainEvent;

/// <summary>Published by Reviews module — Catalog updates <c>rating_avg</c> + <c>rating_count</c>.</summary>
public sealed record PropertyRatingRecomputeRequested(Guid PropertyId) : DomainEvent;
