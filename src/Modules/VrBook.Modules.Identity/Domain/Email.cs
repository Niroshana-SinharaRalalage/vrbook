using System.Text.RegularExpressions;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Normalised email address (lowercased, trimmed). The constructor validates shape;
/// AD B2C is the source of truth for deliverability + verification.
/// </summary>
public sealed partial class Email : ValueObject
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleViolationException(nameof(Email), "Email is required.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!EmailShape().IsMatch(normalized))
        {
            throw new BusinessRuleViolationException(nameof(Email), "Email is not in a valid format.");
        }

        Value = normalized;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(Email email) => email.Value;

    [GeneratedRegex(@"^[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();
}
