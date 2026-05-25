using System.Text.RegularExpressions;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// E.164-style phone. Phase 1 stores raw input but enforces minimum shape;
/// real internationalisation (libphonenumber) is Phase 2.
/// </summary>
public sealed partial class PhoneNumber : ValueObject
{
    public string Value { get; }

    public PhoneNumber(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 0 && !PhoneShape().IsMatch(normalized))
        {
            throw new BusinessRuleViolationException(nameof(PhoneNumber), "Phone number format is invalid.");
        }
        Value = normalized;
    }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\+?[0-9\s\-()]{7,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneShape();
}
