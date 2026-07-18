using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Property aggregate root. Owns images, house rules, and the amenity join.
/// The owner_user_id is a FK to identity.users(id); cross-schema reference
/// permitted per proposal §5.1.
/// </summary>
public sealed class Property : AggregateRoot
{
    public string Slug { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public PropertyType Type { get; private set; }
    public Address Address { get; private set; } = default!;
    public Capacity Capacity { get; private set; } = default!;
    public CheckInWindow CheckInWindow { get; private set; } = default!;

    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Tenant the property belongs to. Per `docs/OPS_M_3_PLAN.md` §3 — populated
    /// from <c>ICurrentUser.TenantId</c> when the owner creates the property.
    /// OPS.M.3c flipped from <c>Guid?</c> to <c>Guid</c> after the backfill
    /// migration left zero null rows; the EF config now declares the column
    /// NOT NULL.
    /// </summary>
    public Guid TenantId { get; private set; }

    public bool IsActive { get; private set; }
    public bool ReviewsEnabled { get; private set; }
    public bool DynamicPricingEnabled { get; private set; }
    public bool MessagingEnabled { get; private set; }

    /// <summary>
    /// Slice OPS.M.16 — default turnover window (hours). Booking.CheckOut()
    /// snapshots this into <c>Booking.CompletionDueAt</c> unless the booking
    /// carries a per-instance override. Owners tune this per-inventory:
    /// downtown studios may set 6-12h; beach villas may set 48h. Domain
    /// caps at 168h (7 days) — see M.16.1 validation.
    /// </summary>
    public int TurnoverHours { get; private set; } = 24;

    public decimal? RatingAvg { get; private set; }
    public int RatingCount { get; private set; }

    /// <summary>
    /// VRB-215 — the host-selected cancellation model for this property (owner-locked
    /// launch set: Tiered | RefundableUpgrade). Nullable: unset ⇒ Tiered (the default),
    /// so existing rows and new properties behave as Tiered until a host chooses. The
    /// per-guest upgrade amount is platform-priced (VRB-216 UpgradePricePct), not stored here.
    /// </summary>
    public CancellationModel? CancellationModel { get; private set; }

    /// <summary>VRB-215 — set the property's cancellation model (tenant-admin).</summary>
    public void SetCancellationModel(CancellationModel model) => CancellationModel = model;

    /// <summary>
    /// Recalculated from the Reviews module via PropertyRatingRecomputeRequested.
    /// Owned by Reviews (A6) — A2 leaves the setter dormant until then.
    /// </summary>
    public void SetRating(decimal? avg, int count)
    {
        RatingAvg = avg;
        RatingCount = count;
    }

    private readonly List<PropertyImage> _images = new();
    public IReadOnlyList<PropertyImage> Images => _images;

    private readonly List<HouseRule> _houseRules = new();
    public IReadOnlyList<HouseRule> HouseRules => _houseRules;

    private readonly List<Guid> _amenityIds = new();
    /// <summary>Amenity Ids associated to this property (join table managed by EF).</summary>
    public IReadOnlyList<Guid> AmenityIds => _amenityIds;

    private Property() { } // EF

    public static Property Create(
        Guid tenantId,
        Guid ownerUserId,
        string title,
        string description,
        PropertyType type,
        Address address,
        Capacity capacity,
        CheckInWindow checkIn,
        IEnumerable<string> houseRules,
        IEnumerable<Guid> amenityIds,
        string slug,
        int turnoverHours = 24)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ValidateTurnoverHours(turnoverHours);

        var p = new Property
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerUserId = ownerUserId,
            Slug = slug,
            Title = title.Trim(),
            Description = description.Trim(),
            Type = type,
            Address = address,
            Capacity = capacity,
            CheckInWindow = checkIn,
            IsActive = false,            // owner activates explicitly after adding photos
            ReviewsEnabled = true,
            DynamicPricingEnabled = false,
            MessagingEnabled = true,
            TurnoverHours = turnoverHours,
        };
        var i = 0;
        foreach (var r in houseRules)
        {
            if (string.IsNullOrWhiteSpace(r))
            {
                continue;
            }

            p._houseRules.Add(new HouseRule(p.Id, r, i++));
        }
        foreach (var aid in amenityIds.Distinct())
        {
            p._amenityIds.Add(aid);
        }

        p.Raise(new PropertyCreated(p.Id, ownerUserId, slug, p.Title, p.TenantId));
        return p;
    }

    public void UpdateBasics(
        string title,
        string description,
        Address address,
        Capacity capacity,
        CheckInWindow checkIn,
        bool reviewsEnabled,
        bool dynamicPricingEnabled,
        bool messagingEnabled,
        int turnoverHours = 24)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateTurnoverHours(turnoverHours);
        Title = title.Trim();
        Description = description.Trim();
        Address = address;
        Capacity = capacity;
        CheckInWindow = checkIn;
        ReviewsEnabled = reviewsEnabled;
        DynamicPricingEnabled = dynamicPricingEnabled;
        MessagingEnabled = messagingEnabled;
        TurnoverHours = turnoverHours;
        Raise(new PropertyUpdated(Id));
    }

    private static void ValidateTurnoverHours(int turnoverHours)
    {
        if (turnoverHours < 0 || turnoverHours > 168)
        {
            throw new BusinessRuleViolationException(
                "property.turnover_hours_out_of_range",
                $"TurnoverHours must be between 0 and 168 (one week); got {turnoverHours}.");
        }
    }

    public void ReplaceHouseRules(IEnumerable<string> rules)
    {
        _houseRules.Clear();
        var i = 0;
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r))
            {
                continue;
            }

            _houseRules.Add(new HouseRule(Id, r, i++));
        }
    }

    public void ReplaceAmenities(IEnumerable<Guid> amenityIds)
    {
        _amenityIds.Clear();
        foreach (var aid in amenityIds.Distinct())
        {
            _amenityIds.Add(aid);
        }
    }

    /// <summary>
    /// Slice OPS.M.10.2 F11.1 — Property publishing precondition.
    /// A property can only go live when its tenant has completed Stripe
    /// Connect onboarding (Status = "Active" AND ChargesEnabled AND
    /// PayoutsEnabled). Pre-F11.1 the no-arg <see cref="Activate()"/>
    /// had ZERO preconditions and let properties go live without a
    /// payment-ready tenant — the booking flow then 422'd at
    /// `CreatePaymentIntentForBookingHandler` with the
    /// <c>payment.connect_account_missing</c> error message that the
    /// audit's #2 finding flagged.
    ///
    /// <para>Pass primitive booleans (NOT a Tenant reference) because
    /// Catalog cannot reference the Identity module — proper layering.
    /// Callers project the tenant snapshot at the controller / handler
    /// boundary.</para>
    /// </summary>
    public void Activate(string tenantStatus, bool tenantChargesEnabled, bool tenantPayoutsEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantStatus);
        if (!string.Equals(tenantStatus, "Active", StringComparison.Ordinal))
        {
            throw new BusinessRuleViolationException(
                "property.tenant_not_payment_ready",
                $"Property cannot be activated while tenant status is '{tenantStatus}'. " +
                "Complete Stripe Connect onboarding first.");
        }
        if (!tenantChargesEnabled || !tenantPayoutsEnabled)
        {
            throw new BusinessRuleViolationException(
                "property.tenant_not_payment_ready",
                "Property cannot be activated until the tenant's Stripe Connect account " +
                $"has charges_enabled (currently {tenantChargesEnabled}) AND payouts_enabled " +
                $"(currently {tenantPayoutsEnabled}).");
        }
        if (_images.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "property.requires_image",
                "Add at least one photo before publishing this listing.");
        }
        IsActive = true;
    }

#pragma warning disable S1133 // F11.1 bridge intentionally deprecated; OPS.M.11 deletes it.
    /// <summary>
    /// Pre-F11.1 no-arg activation. Retained for the in-flight migration
    /// path — existing seed properties have <c>is_active = true</c> via
    /// direct DB writes from earlier slices; flipping the gate retroactively
    /// would mark them inactive. The OPS.M.11 properties-lifecycle slice
    /// will (a) wire a controller publish endpoint that uses the new
    /// gated overload, and (b) DELETE this one.
    /// </summary>
    [System.Obsolete("Slice OPS.M.10.2 F11.1 — use Activate(tenantStatus, chargesEnabled, payoutsEnabled). " +
        "This no-arg overload is retained only for the in-flight migration; new call sites must use the gated form. " +
        "Tracked for deletion in Slice OPS.M.11 properties-lifecycle.")]
    public void Activate() => IsActive = true;
#pragma warning restore S1133
    public void Deactivate(string reason)
    {
        IsActive = false;
        Raise(new PropertyDeactivated(Id, reason));
    }

    public PropertyImage AddImage(string blobPath, string? caption) =>
        AddImage(Guid.NewGuid(), blobPath, caption);

    /// <summary>
    /// VRB-101 — id-carrying overload so the blob filename
    /// (<c>{tenantId}/{propertyId}/{imageId}.ext</c>) and the row id stay
    /// identical/traceable; the handler generates the id, uploads under it,
    /// then records the row.
    /// </summary>
    public PropertyImage AddImage(Guid imageId, string blobPath, string? caption)
    {
        var nextSort = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        var isFirst = _images.Count == 0;
        var img = new PropertyImage(imageId, TenantId, Id, blobPath, caption, nextSort, isFirst);
        _images.Add(img);
        Raise(new PropertyImageAdded(Id, img.Id, blobPath, TenantId));
        return img;
    }

    /// <summary>
    /// VRB-101 — removes an image and returns its blob path so the caller can
    /// delete the blob AFTER the row delete commits (no orphaned blob on a
    /// failed SaveChanges). If the removed image was primary, the lowest
    /// remaining <c>SortOrder</c> is promoted so the gallery always has a cover.
    /// </summary>
    public string RemoveImage(Guid imageId)
    {
        var img = _images.SingleOrDefault(i => i.Id == imageId)
            ?? throw new NotFoundException("PropertyImage", imageId);
        var wasPrimary = img.IsPrimary;
        var blobPath = img.BlobPath;
        _images.Remove(img);

        if (wasPrimary && _images.Count > 0)
        {
            var next = _images.OrderBy(i => i.SortOrder).First();
            next.Promote(next.SortOrder, isPrimary: true);
        }
        Raise(new PropertyImageRemoved(Id, imageId, TenantId));
        return blobPath;
    }

    /// <summary>
    /// VRB-101 — reorders the gallery. <paramref name="orderedIds"/> must be a
    /// permutation of the current image ids; <c>SortOrder</c> becomes the index
    /// and index 0 becomes the primary/cover photo.
    /// </summary>
    public void ReorderImages(IReadOnlyList<Guid> orderedIds)
    {
        var current = _images.Select(i => i.Id).ToHashSet();
        if (orderedIds.Count != current.Count || !orderedIds.All(current.Contains))
        {
            throw new BusinessRuleViolationException(
                "property.image_reorder_mismatch",
                "The reorder request must list exactly the property's current image ids.");
        }
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var img = _images.Single(i => i.Id == orderedIds[index]);
            img.Promote(index, isPrimary: index == 0);
        }
    }
}
