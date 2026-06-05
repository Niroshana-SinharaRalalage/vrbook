using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Amenity lookup. Stable code (e.g. "wifi", "pool") -> display Name + Icon.
/// Seeded by migration; rarely changes after that.
/// </summary>
public sealed class Amenity : AggregateRoot
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Icon { get; private set; }
    public string Category { get; private set; } = default!;

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
    }
}
