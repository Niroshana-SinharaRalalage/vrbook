using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Domain;

/// <summary>
/// Money value object. Amount + ISO-4217 currency code. We use decimal for
/// accuracy; the API serialises as { amount, currency }.
/// </summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO-4217 code.", nameof(currency));
        }
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException("Cannot add Money values in different currencies.");
        }
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int multiplier) => new(Amount * multiplier, Currency);

    private Money() { Currency = string.Empty; } // EF

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
