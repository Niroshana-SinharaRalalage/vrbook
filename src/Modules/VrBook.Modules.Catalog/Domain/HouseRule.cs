using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Owner-defined house rule (e.g. "No smoking", "Quiet hours 22:00-08:00").
/// Free-form text; rendered as a list on the listing page.
/// </summary>
public sealed class HouseRule : Entity
{
    public Guid PropertyId { get; private set; }
    public string RuleText { get; private set; } = default!;
    public int SortOrder { get; private set; }

    private HouseRule() { } // EF

    internal HouseRule(Guid propertyId, string ruleText, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleText);
        Id = Guid.NewGuid();
        PropertyId = propertyId;
        RuleText = ruleText.Trim();
        SortOrder = sortOrder;
    }
}
