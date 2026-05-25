namespace VrBook.Domain.Common;

/// <summary>
/// Base for value objects — equality by structural component comparison. Prefer
/// <c>sealed record</c> in <c>VrBook.Contracts.Common</c> for cross-context VOs;
/// use this base only when serialization or EF mapping requires a class.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && GetType() == other.GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(17, (acc, c) => HashCode.Combine(acc, c?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? a, ValueObject? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);
}
