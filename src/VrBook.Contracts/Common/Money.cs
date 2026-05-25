namespace VrBook.Contracts.Common;

/// <summary>
/// A currency-aware monetary value. Used in DTOs and the pricing engine.
/// </summary>
/// <remarks>
/// Stored as a decimal to preserve fractional cents during multi-step calculation.
/// Currency uses ISO-4217 three-letter codes (e.g. "USD", "EUR").
/// </remarks>
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount + other.Amount };
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount - other.Amount };
    }

    public Money Multiply(decimal factor) => this with { Amount = Amount * factor };

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Currency mismatch: {Currency} vs {other.Currency}");
        }
    }
}
