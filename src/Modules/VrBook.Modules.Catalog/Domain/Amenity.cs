using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Amenity lookup. Stable code (e.g. "wifi", "pool") -> display Name + Icon.
/// Seeded by migration. Owner-attached via property_amenities join. Admin can
/// add / edit / disable through /api/v1/admin/amenities (A2.2). Public
/// `GET /amenities` excludes <see cref="IsActive"/>=false so disabled amenities
/// stop appearing on the front-end without breaking the historical FK from
/// existing properties.
/// </summary>
public sealed class Amenity : AggregateRoot
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Icon { get; private set; }
    public string Category { get; private set; } = default!;

    /// <summary>True iff the amenity is visible in the public catalog. Defaults true.</summary>
    public bool IsActive { get; private set; } = true;

    private Amenity() { } // EF

    public Amenity(Guid id, string code, string name, string? icon, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Id = id;
        Code = code.Trim().ToLowerInvariant();
        Name = name.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Category = category.Trim();
        IsActive = true;
    }

    /// <summary>
    /// Overwrites display fields. <see cref="Code"/> is intentionally immutable —
    /// changing it would break the property_amenities join + any client cache keyed
    /// on the code string.
    /// </summary>
    public void Update(string name, string? icon, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Name = name.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Category = category.Trim();
    }

    /// <summary>Hide from the public catalog. Idempotent. Property attachments remain.</summary>
    public void Disable() => IsActive = false;

    /// <summary>Show in the public catalog. Idempotent.</summary>
    public void Enable() => IsActive = true;
}
