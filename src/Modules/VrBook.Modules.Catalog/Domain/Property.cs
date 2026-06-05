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

    public bool IsActive { get; private set; }
    public bool ReviewsEnabled { get; private set; }
    public bool DynamicPricingEnabled { get; private set; }
    public bool MessagingEnabled { get; private set; }

    public decimal? RatingAvg { get; private set; }
    public int RatingCount { get; private set; }

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
        Guid ownerUserId,
        string title,
        string description,
        PropertyType type,
        Address address,
        Capacity capacity,
        CheckInWindow checkIn,
        IEnumerable<string> houseRules,
        IEnumerable<Guid> amenityIds,
        string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var p = new Property
        {
            Id = Guid.NewGuid(),
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

        p.Raise(new PropertyCreated(p.Id, ownerUserId, slug, p.Title));
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
        bool messagingEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Title = title.Trim();
        Description = description.Trim();
        Address = address;
        Capacity = capacity;
        CheckInWindow = checkIn;
        ReviewsEnabled = reviewsEnabled;
        DynamicPricingEnabled = dynamicPricingEnabled;
        MessagingEnabled = messagingEnabled;
        Raise(new PropertyUpdated(Id));
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

    public void Activate() => IsActive = true;
    public void Deactivate(string reason)
    {
        IsActive = false;
        Raise(new PropertyDeactivated(Id, reason));
    }

    public PropertyImage AddImage(string blobPath, string? caption)
    {
        var nextSort = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        var isFirst = _images.Count == 0;
        var img = new PropertyImage(Id, blobPath, caption, nextSort, isFirst);
        _images.Add(img);
        Raise(new PropertyImageAdded(Id, img.Id, blobPath));
        return img;
    }
}
